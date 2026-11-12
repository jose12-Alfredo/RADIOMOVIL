import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:radiomovil_conductor/core/api/api_exception.dart';
import 'package:radiomovil_conductor/features/conductor/conductor_models.dart';
import 'package:radiomovil_conductor/features/conductor/conductor_repository.dart';
import 'package:radiomovil_conductor/features/tracking/location_source.dart';

typedef Handler = FutureOr<ResponseBody> Function(RequestOptions options, String? body);

/// Adaptador HTTP de pruebas: responde según la ruta, sin red.
class FakeAdapter implements HttpClientAdapter {
  FakeAdapter(this.handler);
  final Handler handler;
  final requests = <RequestOptions>[];

  static ResponseBody json(Object? data, [int status = 200, Map<String, List<String>> headers = const {}]) =>
      ResponseBody.fromString(data == null ? '' : jsonEncode(data), status, headers: {
        Headers.contentTypeHeader: [Headers.jsonContentType],
        ...headers,
      });

  @override
  Future<ResponseBody> fetch(RequestOptions options, Stream<Uint8List>? requestStream, Future<void>? cancelFuture) async {
    requests.add(options);
    String? body;
    if (requestStream != null) body = utf8.decode(await requestStream.expand((c) => c).toList());
    return handler(options, body);
  }

  @override
  void close({bool force = false}) {}
}

class FakeConductorApi implements ConductorApi {
  final enviados = <UbicacionCapturada>[];
  final clavesAbrir = <String>[];
  final clavesCerrar = <String>[];
  Object? errorEnvio;
  Object? errorAbrir;
  Object? errorCerrar;
  List<String> roles = ['Conductor'];
  ConductorPropio yoActual = conductor();
  List<CarreraAsignada> carrerasActuales = [];
  int llamadasCarreras = 0;
  final acuses = <String>[];
  Object? errorAcuse;

  /// Simula que la central asigna (o cierra con null) una carrera.
  void asignar(CarreraAsignada? c) {
    carrerasActuales = c == null ? [] : [c];
    yoActual = ConductorPropio(
      conductorId: yoActual.conductorId,
      nombre: yoActual.nombre,
      estado: c == null ? EstadoConductor.disponible : EstadoConductor.ocupado,
      ocupacion: c == null ? null : 'Viaje',
      turno: yoActual.turno,
      estadoGps: yoActual.estadoGps,
      ultimaRecepcion: null,
      intervaloEnvioSegundos: 5,
      carreraActivaId: c?.viajeId,
    );
  }

  static CarreraAsignada carrera({
    String id = 'viaje-1',
    bool conCoordenadas = true,
    String? telefono = '+56 9 1234 5678',
    DateTime? recibidaEn,
  }) =>
      CarreraAsignada.fromJson({
        'viajeId': id,
        'solicitudId': 's1',
        'estado': 'Asignado',
        'asignadoEn': '2026-11-10T15:00:00Z',
        'recibidaEn': recibidaEn?.toIso8601String(),
        'origen': 'Av. Central 123',
        'recogidaLatitud': conCoordenadas ? -33.45 : null,
        'recogidaLongitud': conCoordenadas ? -70.66 : null,
        'destino': 'Terminal Sur',
        'observaciones': 'Portón azul',
        'nombrePasajero': 'Ana',
        'telefonoPasajero': telefono,
      });

  @override
  Future<List<CarreraAsignada>> carreras() async {
    llamadasCarreras++;
    return carrerasActuales;
  }

  @override
  Future<DateTime> marcarRecibida(String viajeId) async {
    if (errorAcuse case final e?) {
      errorAcuse = null;
      throw e;
    }
    acuses.add(viajeId);
    return DateTime.utc(2026, 11, 10, 15, 1);
  }

  static ConductorPropio conductor({TurnoActivo? turno}) => ConductorPropio(
        conductorId: '11111111-1111-1111-1111-111111111111',
        nombre: 'Jorge',
        estado: turno == null ? EstadoConductor.fueraDeServicio : EstadoConductor.disponible,
        ocupacion: null,
        turno: turno,
        estadoGps: EstadoGps.sinSenal,
        ultimaRecepcion: null,
        intervaloEnvioSegundos: 5,
      );

  @override
  Future<Sesion> sesion() async =>
      Sesion(usuarioId: 'u', email: 'jorge@example.com', roles: roles, expiraEn: DateTime.utc(2030));

  @override
  Future<ConductorPropio> yo() async => yoActual;

  @override
  Future<List<VehiculoAutorizado>> vehiculos() async =>
      const [VehiculoAutorizado(vehiculoId: 'v1', patente: 'AUTOTOYOTA', libre: true)];

  @override
  Future<void> abrirTurno(String vehiculoId, {required String idempotencyKey}) async {
    clavesAbrir.add(idempotencyKey);
    if (errorAbrir case final e?) {
      errorAbrir = null;
      throw e;
    }
    yoActual = conductor(
        turno: TurnoActivo(id: 't1', vehiculoId: vehiculoId, patente: 'AUTOTOYOTA', inicio: DateTime.utc(2026, 11, 9, 12)));
  }

  @override
  Future<void> cerrarTurno({String? motivo, required String idempotencyKey}) async {
    clavesCerrar.add(idempotencyKey);
    if (errorCerrar case final e?) {
      errorCerrar = null;
      throw e;
    }
    yoActual = conductor();
  }

  @override
  Future<ResultadoUbicacion> enviarUbicacion(UbicacionCapturada u) async {
    if (errorEnvio case final e?) {
      errorEnvio = null;
      throw e;
    }
    enviados.add(u);
    return ResultadoUbicacion(
        aceptada: true, motivo: null, recibidaEn: u.capturadaEn, estadoGps: EstadoGps.reciente, intervaloMinimoSegundos: 3);
  }
}

class FakeLocation implements LocationSource {
  PermisoUbicacion permiso = PermisoUbicacion.concedido;
  final controller = StreamController<PosicionDispositivo>.broadcast();

  void emitir(PosicionDispositivo p) => controller.add(p);

  @override
  Future<PermisoUbicacion> estado() async => permiso;
  @override
  Future<PermisoUbicacion> solicitar() async => permiso;
  @override
  Stream<PosicionDispositivo> posiciones({required Duration intervalo}) => controller.stream;
  @override
  Future<bool> abrirAjustesApp() async => true;
  @override
  Future<bool> abrirAjustesUbicacion() async => true;
}

/// Reloj manipulable.
class FakeClock {
  FakeClock(this.now);
  DateTime now;
  DateTime call() => now;
  void advance(Duration d) => now = now.add(d);
}

const network = NetworkException();
