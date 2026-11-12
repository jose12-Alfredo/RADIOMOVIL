import 'package:dio/dio.dart';

import '../../core/api/api_client.dart';
import 'conductor_models.dart';

/// Acceso a los contratos existentes del conductor. El conductor lo deduce
/// el servidor de la sesión: nunca se envía su id.
abstract interface class ConductorApi {
  Future<Sesion> sesion();
  Future<ConductorPropio> yo();
  Future<List<VehiculoAutorizado>> vehiculos();
  Future<void> abrirTurno(String vehiculoId, {required String idempotencyKey});
  Future<void> cerrarTurno({String? motivo, required String idempotencyKey});
  Future<ResultadoUbicacion> enviarUbicacion(UbicacionCapturada ubicacion);

  /// Carreras activas asignadas por la central (hoy 0 o 1).
  Future<List<CarreraAsignada>> carreras();

  /// Acuse de ENTREGA (no aceptación): la orden se mostró en pantalla.
  /// Idempotente en el servidor; devuelve `recibidaEn`.
  Future<DateTime> marcarRecibida(String viajeId);
}

class ConductorRepository implements ConductorApi {
  ConductorRepository(this._client);
  final ApiClient _client;

  Dio get _http => _client.http;

  Future<T> _run<T>(Future<T> Function() call) async {
    try {
      return await call();
    } on DioException catch (e) {
      throw toApiException(e);
    }
  }

  static Options _idem(String key) => Options(headers: {'Idempotency-Key': key});

  @override
  Future<Sesion> sesion() => _run(() async =>
      Sesion.fromJson((await _http.get<Map<String, dynamic>>('api/v1/auth/me')).data!));

  @override
  Future<ConductorPropio> yo() => _run(() async =>
      ConductorPropio.fromJson((await _http.get<Map<String, dynamic>>('api/v1/conductor/yo')).data!));

  @override
  Future<List<VehiculoAutorizado>> vehiculos() => _run(() async {
        final data = (await _http.get<List<dynamic>>('api/v1/conductor/vehiculos')).data ?? const [];
        return data.whereType<Map<String, dynamic>>().map(VehiculoAutorizado.fromJson).toList();
      });

  @override
  Future<void> abrirTurno(String vehiculoId, {required String idempotencyKey}) => _run(() =>
      _http.post<void>('api/v1/conductor/turno', data: {'vehiculoId': vehiculoId}, options: _idem(idempotencyKey)));

  @override
  Future<void> cerrarTurno({String? motivo, required String idempotencyKey}) => _run(() => _http.post<void>(
      'api/v1/conductor/turno/cerrar',
      data: {'motivo': (motivo?.trim().isEmpty ?? true) ? null : motivo!.trim()},
      options: _idem(idempotencyKey)));

  @override
  Future<ResultadoUbicacion> enviarUbicacion(UbicacionCapturada ubicacion) => _run(() async =>
      ResultadoUbicacion.fromJson((await _http.post<Map<String, dynamic>>('api/v1/conductor/ubicaciones',
              data: ubicacion.toJson()))
          .data!));

  @override
  Future<List<CarreraAsignada>> carreras() => _run(() async {
        final data = (await _http.get<List<dynamic>>('api/v1/conductor/carreras')).data ?? const [];
        return data.whereType<Map<String, dynamic>>().map(CarreraAsignada.fromJson).toList();
      });

  @override
  Future<DateTime> marcarRecibida(String viajeId) => _run(() async {
        final data = (await _http.post<Map<String, dynamic>>(
                'api/v1/conductor/carreras/${Uri.encodeComponent(viajeId)}/recibida'))
            .data;
        final en = data?['recibidaEn'];
        return en is String ? DateTime.parse(en) : DateTime.now();
      });
}
