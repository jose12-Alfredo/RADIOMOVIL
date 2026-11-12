import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:uuid/uuid.dart';

import '../../core/api/api_client.dart';
import '../../core/api/api_exception.dart';
import '../conductor/conductor_models.dart';
import '../conductor/conductor_repository.dart';
import '../tracking/tracking_controller.dart';

enum FaseSesion { iniciando, sinSesion, cargando, lista }

/// Coordina sesión, perfil (`/conductor/yo`), turno, seguimiento GPS y la
/// carrera asignada por la central (asignación directa: sin aceptar/rechazar).
/// El estado del conductor siempre es el último confirmado por el servidor.
class DriverController extends ChangeNotifier {
  DriverController({
    required ApiClient client,
    required ConductorApi api,
    required this.empresaId,
    required TrackingController Function(DriverController) trackingFactory,
    Uuid? uuid,
  })  : _client = client,
        _api = api,
        _uuid = uuid ?? const Uuid() {
    tracking = trackingFactory(this);
    _expired = _client.sessionExpired.listen((_) => _cerrarLocal(const UnauthorizedException().message));
  }

  final ApiClient _client;
  final ConductorApi _api;
  final Uuid _uuid;
  final String empresaId;
  late final TrackingController tracking;
  late final StreamSubscription<void> _expired;
  Timer? _poll;

  FaseSesion fase = FaseSesion.iniciando;
  Sesion? sesion;
  ConductorPropio? yo;
  DateTime? actualizadoEn;
  String? error;
  bool operando = false;
  bool sinConexion = false;

  /// Sondeo de `/conductor/yo` en primer plano (contrato: ~10 s, sin FCM).
  static const intervaloSondeo = Duration(seconds: 10);

  /// Carrera activa asignada por la central, o null.
  CarreraAsignada? carrera;

  /// Carrera recién llegada que la pantalla debe anunciar UNA vez (sonido y
  /// vibración). Al mostrarla, la pantalla llama a [anuncioMostrado].
  CarreraAsignada? porAnunciar;
  final _anunciadas = <String>{};
  final _acusesPendientes = <String>{};
  bool _enviandoAcuses = false;

  // Claves de idempotencia: se reutilizan en los reintentos de la MISMA intención.
  String? _claveAbrir;
  String? _vehiculoClaveAbrir;
  String? _claveCerrar;

  Future<void> restaurar() async {
    if (!await _client.hasSession()) {
      fase = FaseSesion.sinSesion;
      notifyListeners();
      return;
    }
    fase = FaseSesion.cargando;
    notifyListeners();
    await _cargarInicial();
  }

  Future<void> login(String email, String password) async {
    error = null;
    operando = true;
    notifyListeners();
    try {
      await _client.login(email: email.trim(), password: password, empresaId: empresaId);
      await _cargarInicial();
    } on ApiException catch (e) {
      error = e.message;
      fase = FaseSesion.sinSesion;
    } finally {
      operando = false;
      notifyListeners();
    }
  }

  Future<void> _cargarInicial() async {
    try {
      final s = await _api.sesion();
      if (!s.roles.contains('Conductor')) {
        await _client.logout();
        throw const ForbiddenException();
      }
      sesion = s;
      await _cargarYo(forzarCarreras: true);
      fase = FaseSesion.lista;
      _poll?.cancel();
      _poll = Timer.periodic(intervaloSondeo, (_) => refrescar(silencioso: true));
    } on NetworkException catch (e) {
      // Con sesión guardada y sin red: se queda cargando hasta recuperar conexión.
      sinConexion = true;
      error = e.message;
      fase = await _client.hasSession() ? FaseSesion.cargando : FaseSesion.sinSesion;
    } on ApiException catch (e) {
      error = e.message;
      if (e is UnauthorizedException || e is ForbiddenException) await _client.logout();
      fase = FaseSesion.sinSesion;
    }
    notifyListeners();
  }

  Future<void> _cargarYo({bool forzarCarreras = false}) async {
    yo = await _api.yo();
    actualizadoEn = DateTime.now();
    sinConexion = false;
    _sincronizarSeguimiento();
    await _sincronizarCarrera(forzar: forzarCarreras);
  }

  /// `GET /carreras` al abrir, al reconectar y cuando `carreraActivaId` cambia.
  Future<void> _sincronizarCarrera({bool forzar = false}) async {
    final id = yo?.carreraActivaId;
    if (!forzar && id == carrera?.viajeId) {
      unawaited(_enviarAcuses());
      return;
    }
    if (!forzar && id == null) {
      carrera = null;
      return;
    }
    try {
      final lista = await _api.carreras();
      final nueva = lista.isEmpty
          ? null
          : lista.firstWhere((c) => c.viajeId == id, orElse: () => lista.first);
      carrera = nueva;
      if (nueva != null && nueva.recibidaEn == null && _anunciadas.add(nueva.viajeId)) {
        porAnunciar = nueva;
      }
    } on ApiException {
      // Se conserva lo último confirmado; el próximo sondeo vuelve a intentarlo.
    }
    unawaited(_enviarAcuses());
  }

  /// La pantalla ya mostró y anunció la orden: se acusa la ENTREGA al servidor.
  void anuncioMostrado() {
    final c = porAnunciar;
    porAnunciar = null;
    if (c == null) return;
    _acusesPendientes.add(c.viajeId);
    unawaited(_enviarAcuses());
  }

  Future<void> _enviarAcuses() async {
    if (_enviandoAcuses || _acusesPendientes.isEmpty) return;
    _enviandoAcuses = true;
    try {
      for (final id in _acusesPendientes.toList()) {
        try {
          final en = await _api.marcarRecibida(id);
          _acusesPendientes.remove(id);
          if (carrera?.viajeId == id) {
            carrera = carrera!.recibida(en);
            notifyListeners();
          }
        } on NetworkException {
          break; // Se reintenta en el próximo sondeo o al reconectar.
        } on ApiException {
          // 404/409: la carrera ya no es de este conductor o se cerró; el
          // próximo `/yo` lo refleja.
          _acusesPendientes.remove(id);
        }
      }
    } finally {
      _enviandoAcuses = false;
    }
  }

  void _sincronizarSeguimiento() {
    final turno = yo?.turno;
    if (turno != null && !tracking.activo) {
      unawaited(tracking.iniciar(intervaloSegundos: yo!.intervaloEnvioSegundos));
    } else if (turno == null && tracking.activo) {
      tracking.detener();
    }
  }

  Future<void> refrescar({bool silencioso = false, bool carreras = false}) async {
    if (fase == FaseSesion.cargando && sesion == null) return _cargarInicial();
    if (fase != FaseSesion.lista) return;
    try {
      await _cargarYo(forzarCarreras: carreras);
      if (!silencioso) error = null;
    } on NetworkException catch (e) {
      sinConexion = true;
      if (!silencioso) error = e.message;
    } on ApiException catch (e) {
      if (!silencioso) error = e.message;
    }
    notifyListeners();
  }

  void conexionRecuperada() {
    sinConexion = false;
    unawaited(refrescar(silencioso: true, carreras: true));
    tracking.conexionRecuperada();
  }

  void conexionPerdida() {
    sinConexion = true;
    notifyListeners();
  }

  Future<List<VehiculoAutorizado>> vehiculos() => _api.vehiculos();

  /// Devuelve null si se abrió, o el mensaje de error.
  Future<String?> abrirTurno(String vehiculoId) => _operar(() async {
        if (_vehiculoClaveAbrir != vehiculoId) {
          _claveAbrir = _uuid.v4();
          _vehiculoClaveAbrir = vehiculoId;
        }
        await _api.abrirTurno(vehiculoId, idempotencyKey: _claveAbrir!);
        _claveAbrir = null;
        _vehiculoClaveAbrir = null;
      }, onFallo: (e) {
        if (e is! NetworkException) {
          _claveAbrir = null;
          _vehiculoClaveAbrir = null;
        }
      });

  Future<String?> cerrarTurno(String? motivo) => _operar(() async {
        _claveCerrar ??= _uuid.v4();
        await _api.cerrarTurno(motivo: motivo, idempotencyKey: _claveCerrar!);
        _claveCerrar = null;
      }, onFallo: (e) {
        if (e is! NetworkException) _claveCerrar = null;
      });

  Future<String?> _operar(Future<void> Function() op, {required void Function(ApiException) onFallo}) async {
    operando = true;
    notifyListeners();
    try {
      await op();
      await _cargarYo();
      error = null;
      return null;
    } on ApiException catch (e) {
      onFallo(e);
      if (e is NetworkException) sinConexion = true;
      // Tras un conflicto el estado real puede haber cambiado: se relee.
      if (e is ConflictException) {
        try {
          await _cargarYo();
        } on ApiException {
          // Se conserva el último estado confirmado.
        }
      }
      return e.message;
    } finally {
      operando = false;
      notifyListeners();
    }
  }

  Future<void> logout() async {
    tracking.detener();
    _poll?.cancel();
    await _client.logout();
    _limpiar();
    notifyListeners();
  }

  void _cerrarLocal(String mensaje) {
    tracking.detener();
    _poll?.cancel();
    _limpiar();
    error = mensaje;
    notifyListeners();
  }

  void _limpiar() {
    fase = FaseSesion.sinSesion;
    sesion = null;
    yo = null;
    carrera = null;
    porAnunciar = null;
    _anunciadas.clear();
    _acusesPendientes.clear();
    _claveAbrir = null;
    _vehiculoClaveAbrir = null;
    _claveCerrar = null;
  }

  @override
  void dispose() {
    _expired.cancel();
    _poll?.cancel();
    tracking.dispose();
    super.dispose();
  }
}
