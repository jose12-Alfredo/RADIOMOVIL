import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/foundation.dart';

import '../../core/api/api_exception.dart';
import '../conductor/conductor_models.dart';
import '../conductor/conductor_repository.dart';
import 'location_source.dart';

enum EstadoConexion { desconocida, enLinea, sinConexion }

/// Seguimiento GPS durante el turno autorizado.
///
/// Reglas:
/// - Solo envía posiciones reales del sistema; descarta las simuladas.
/// - Nunca envía una posición vieja (>60 s) como actual: el servidor rechaza >120 s
///   y la app espera una nueva lectura.
/// - No acumula historial sin red: al reconectar envía solo la última posición.
/// - 409 (sin turno abierto) detiene el seguimiento; 429 respeta Retry-After.
/// - Solo primer plano: al pasar a segundo plano se pausa y se informa.
class TrackingController extends ChangeNotifier {
  TrackingController({
    required ConductorApi api,
    required LocationSource location,
    DateTime Function()? clock,
    this.autoTick = true,
    this.onTurnoCerrado,
  })  : _api = api,
        _location = location,
        _now = clock ?? DateTime.now;

  final ConductorApi _api;
  final LocationSource _location;
  final DateTime Function() _now;
  final bool autoTick;

  /// Se invoca cuando el servidor indica que ya no hay turno abierto.
  final VoidCallback? onTurnoCerrado;

  static const maxEdadPosicion = Duration(seconds: 60);
  static const maxEsperaReintento = Duration(seconds: 30);

  StreamSubscription<PosicionDispositivo>? _sub;
  Timer? _timer;
  Duration _intervalo = const Duration(seconds: 5);
  DateTime? _siguienteEnvio;
  int _fallosSeguidos = 0;
  bool _enviando = false;
  bool _disposed = false;

  bool activo = false;
  bool pausadoSegundoPlano = false;
  PermisoUbicacion? permiso;
  PosicionDispositivo? ultimaPosicion;
  PosicionDispositivo? ultimaEnviada;
  DateTime? ultimoEnvioAceptado;
  DateTime? ultimaRecibidaServidor;
  EstadoConexion conexion = EstadoConexion.desconocida;
  String? aviso;

  /// Frescura de lo que la central tiene de este conductor (reloj local desde el último 200).
  EstadoGps get estadoGps =>
      EstadoGps.clasificar(ultimoEnvioAceptado == null ? null : _now().difference(ultimoEnvioAceptado!));

  Duration? get edadUltimoEnvio => ultimoEnvioAceptado == null ? null : _now().difference(ultimoEnvioAceptado!);

  Future<PermisoUbicacion> comprobarPermiso() async {
    permiso = await _location.estado();
    _notify();
    return permiso!;
  }

  Future<PermisoUbicacion> solicitarPermiso() async {
    permiso = await _location.solicitar();
    _notify();
    if (permiso == PermisoUbicacion.concedido && activo && _sub == null) _suscribir();
    return permiso!;
  }

  Future<void> abrirAjustes() async {
    if (permiso == PermisoUbicacion.servicioApagado) {
      await _location.abrirAjustesUbicacion();
    } else {
      await _location.abrirAjustesApp();
    }
  }

  /// Inicia el seguimiento. Si falta permiso queda activo pero sin lecturas.
  Future<void> iniciar({required int intervaloSegundos}) async {
    _intervalo = Duration(seconds: math.max(5, intervaloSegundos));
    activo = true;
    pausadoSegundoPlano = false;
    _siguienteEnvio = _now();
    aviso = null;
    await comprobarPermiso();
    if (permiso == PermisoUbicacion.concedido) _suscribir();
    if (autoTick) {
      _timer?.cancel();
      _timer = Timer.periodic(const Duration(seconds: 1), (_) => tick());
    }
    _notify();
  }

  void detener() {
    activo = false;
    _cancelar();
    _timer?.cancel();
    _timer = null;
    _notify();
  }

  /// Ciclo de vida: Android no garantiza ubicación en segundo plano sin servicio dedicado.
  void pausarPorSegundoPlano() {
    if (!activo) return;
    pausadoSegundoPlano = true;
    _cancelar();
    _notify();
  }

  Future<void> reanudar() async {
    if (!activo) return;
    pausadoSegundoPlano = false;
    await comprobarPermiso();
    if (permiso == PermisoUbicacion.concedido) _suscribir();
    _siguienteEnvio = _now();
    _notify();
  }

  /// Llamado al detectar que vuelve la red.
  void conexionRecuperada() {
    if (!activo) return;
    _fallosSeguidos = 0;
    _siguienteEnvio = _now();
    unawaited(tick());
  }

  void _suscribir() {
    _sub?.cancel();
    _sub = _location.posiciones(intervalo: _intervalo).listen((p) {
      ultimaPosicion = p;
      _notify();
    }, onError: (Object _) {
      aviso = 'El GPS del teléfono no responde. Comprueba que la ubicación esté activada.';
      _notify();
    });
  }

  void _cancelar() {
    _sub?.cancel();
    _sub = null;
  }

  /// Un paso del planificador: decide si toca enviar y lo hace. Público para pruebas.
  Future<void> tick() async {
    if (!activo || pausadoSegundoPlano || _enviando) {
      _notify();
      return;
    }
    final ahora = _now();
    if (_siguienteEnvio != null && ahora.isBefore(_siguienteEnvio!)) {
      _notify();
      return;
    }
    final p = ultimaPosicion;
    if (p == null) {
      _notify();
      return;
    }
    if (p.simulada) {
      aviso = 'El teléfono usa una ubicación simulada. Desactívala para compartir tu ubicación real.';
      _siguienteEnvio = ahora.add(_intervalo);
      _notify();
      return;
    }
    if (ahora.difference(p.capturadaEn) > maxEdadPosicion) {
      aviso = 'Esperando una lectura GPS actual…';
      _siguienteEnvio = ahora.add(const Duration(seconds: 2));
      _notify();
      return;
    }
    if (identical(p, ultimaEnviada)) {
      // Sin lectura nueva desde el último envío: no se repite la misma captura.
      _siguienteEnvio = ahora.add(const Duration(seconds: 1));
      _notify();
      return;
    }
    await _enviar(p);
  }

  Future<void> _enviar(PosicionDispositivo p) async {
    _enviando = true;
    try {
      final r = await _api.enviarUbicacion(UbicacionCapturada(
        latitud: p.latitud,
        longitud: p.longitud,
        precisionMetros: p.precisionMetros <= 0 ? 1 : p.precisionMetros,
        capturadaEn: p.capturadaEn,
      ));
      ultimaEnviada = p;
      conexion = EstadoConexion.enLinea;
      _fallosSeguidos = 0;
      ultimaRecibidaServidor = r.recibidaEn;
      if (r.aceptada) {
        ultimoEnvioAceptado = _now();
        aviso = null;
      }
      _siguienteEnvio = _now().add(_intervalo);
    } on NetworkException {
      conexion = EstadoConexion.sinConexion;
      _fallosSeguidos++;
      final espera = Duration(seconds: math.min(maxEsperaReintento.inSeconds, _intervalo.inSeconds * (1 << math.min(_fallosSeguidos - 1, 3))));
      _siguienteEnvio = _now().add(espera);
    } on TooManyRequestsException catch (e) {
      conexion = EstadoConexion.enLinea;
      _siguienteEnvio = _now().add(e.retryAfter);
    } on ConflictException {
      conexion = EstadoConexion.enLinea;
      aviso = 'No tienes un turno abierto: se detuvo el envío de ubicación.';
      detener();
      onTurnoCerrado?.call();
    } on BadRequestException catch (e) {
      conexion = EstadoConexion.enLinea;
      ultimaEnviada = p;
      aviso = e.message;
      _siguienteEnvio = _now().add(_intervalo);
    } on UnauthorizedException {
      detener();
    } on ApiException catch (e) {
      aviso = e.message;
      _fallosSeguidos++;
      _siguienteEnvio = _now().add(maxEsperaReintento);
    } finally {
      _enviando = false;
      _notify();
    }
  }

  void _notify() {
    if (!_disposed) notifyListeners();
  }

  @override
  void dispose() {
    _disposed = true;
    _cancelar();
    _timer?.cancel();
    super.dispose();
  }
}
