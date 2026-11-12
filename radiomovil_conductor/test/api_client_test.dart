import 'dart:async';
import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:radiomovil_conductor/core/api/api_client.dart';
import 'package:radiomovil_conductor/core/api/api_exception.dart';
import 'package:radiomovil_conductor/core/auth/token_store.dart';
import 'package:radiomovil_conductor/features/conductor/conductor_models.dart';
import 'package:radiomovil_conductor/features/conductor/conductor_repository.dart';

import 'fakes.dart';

Map<String, Object> tokens(String access, String refresh) =>
    {'tokenType': 'Bearer', 'accessToken': access, 'expiresIn': 600, 'refreshToken': refresh};

void main() {
  final now = DateTime.utc(2026, 11, 9, 12);
  late MemoryTokenStore store;

  setUp(() => store = MemoryTokenStore());

  ApiClient client(FakeAdapter adapter) =>
      ApiClient(baseUrl: Uri.parse('https://api.test/'), tokenStore: store, adapter: adapter, clock: () => now);

  test('login Android guarda tokens y envía empresa y cliente', () async {
    final adapter = FakeAdapter((o, body) {
      expect(o.path, 'api/v1/auth/login');
      expect(o.headers['Authorization'], isNull);
      final json = jsonDecode(body!) as Map<String, dynamic>;
      expect(json['cliente'], 'Android');
      expect(json['empresaId'], 'emp');
      return FakeAdapter.json(tokens('a1', 'r1'));
    });
    await client(adapter).login(email: 'j@x.bo', password: 'p', empresaId: 'emp');
    expect(store.tokens!.accessToken, 'a1');
    expect(store.tokens!.accessExpiresAt, now.add(const Duration(minutes: 10)));
  });

  test('login con 401 informa credenciales inválidas sin guardar nada', () async {
    final adapter = FakeAdapter((o, _) => FakeAdapter.json(null, 401));
    await expectLater(client(adapter).login(email: 'j@x.bo', password: 'p', empresaId: 'emp'),
        throwsA(isA<InvalidCredentialsException>()));
    expect(store.tokens, isNull);
  });

  test('401 concurrentes comparten UNA sola renovación (refresh de un solo uso)', () async {
    store.tokens = AuthTokens(accessToken: 'viejo', refreshToken: 'r1', accessExpiresAt: now.add(const Duration(minutes: 5)));
    var refreshes = 0;
    final refreshGate = Completer<void>();
    final adapter = FakeAdapter((o, body) async {
      if (o.path == 'api/v1/auth/refresh') {
        refreshes++;
        expect(jsonDecode(body!)['refreshToken'], 'r1');
        await refreshGate.future;
        return FakeAdapter.json(tokens('nuevo', 'r2'));
      }
      if (o.headers['Authorization'] == 'Bearer nuevo') {
        return FakeAdapter.json({
          'conductorId': 'c', 'nombre': 'Jorge', 'estado': 'FueraDeServicio', 'ocupacion': null, 'turno': null,
          'estadoGps': 'SinSenal', 'ultimaRecepcion': null, 'intervaloEnvioSegundos': 5,
        });
      }
      return FakeAdapter.json(null, 401);
    });
    final repo = ConductorRepository(client(adapter));
    final calls = [repo.yo(), repo.yo(), repo.yo()];
    await Future<void>.delayed(const Duration(milliseconds: 20));
    refreshGate.complete();
    final results = await Future.wait(calls);
    expect(results.map((r) => r.estado), everyElement(EstadoConductor.fueraDeServicio));
    expect(refreshes, 1);
    expect(store.tokens!.refreshToken, 'r2');
  });

  test('refresh rechazado cierra la sesión y la notifica', () async {
    store.tokens = AuthTokens(accessToken: 'a', refreshToken: 'r', accessExpiresAt: now);
    final adapter = FakeAdapter((o, _) => FakeAdapter.json(null, 401));
    final api = client(adapter);
    final expired = expectLater(api.sessionExpired, emits(null));
    await expectLater(ConductorRepository(api).yo(), throwsA(isA<UnauthorizedException>()));
    await expired;
    expect(store.tokens, isNull);
  });

  test('sin red al renovar se conserva la sesión', () async {
    store.tokens = AuthTokens(accessToken: 'a', refreshToken: 'r', accessExpiresAt: now);
    final adapter = FakeAdapter((o, _) => throw const SocketLikeError());
    await expectLater(ConductorRepository(client(adapter)).yo(), throwsA(isA<ApiException>()));
    expect(store.tokens, isNotNull);
  });

  test('turno envía Idempotency-Key y ubicación el contrato exacto', () async {
    store.tokens = AuthTokens(accessToken: 'a', refreshToken: 'r', accessExpiresAt: now.add(const Duration(minutes: 5)));
    final adapter = FakeAdapter((o, body) {
      if (o.path == 'api/v1/conductor/turno') {
        expect(o.headers['Idempotency-Key'], 'k-1');
        expect(jsonDecode(body!), {'vehiculoId': 'v1'});
        return FakeAdapter.json({'id': 't', 'repetida': false}, 201);
      }
      expect(o.path, 'api/v1/conductor/ubicaciones');
      expect(jsonDecode(body!), {
        'latitud': -16.5, 'longitud': -68.15, 'precisionMetros': 8.0, 'capturadaEn': '2026-11-09T12:00:00.000Z',
      });
      return FakeAdapter.json({'aceptada': true, 'motivo': null, 'recibidaEn': '2026-11-09T12:00:01Z', 'estadoGps': 'Reciente', 'intervaloMinimoSegundos': 3});
    });
    final repo = ConductorRepository(client(adapter));
    await repo.abrirTurno('v1', idempotencyKey: 'k-1');
    final r = await repo.enviarUbicacion(UbicacionCapturada(latitud: -16.5, longitud: -68.15, precisionMetros: 8, capturadaEn: now));
    expect(r.aceptada, isTrue);
    expect(r.estadoGps, EstadoGps.reciente);
  });

  test('429 expone Retry-After y 409 el mensaje del servidor', () async {
    store.tokens = AuthTokens(accessToken: 'a', refreshToken: 'r', accessExpiresAt: now.add(const Duration(minutes: 5)));
    var n = 0;
    final adapter = FakeAdapter((o, _) => n++ == 0
        ? FakeAdapter.json('Envío demasiado frecuente.', 429, {'retry-after': ['2']})
        : FakeAdapter.json('No tienes un turno abierto.', 409));
    final repo = ConductorRepository(client(adapter));
    final u = UbicacionCapturada(latitud: 0, longitud: 0, precisionMetros: 5, capturadaEn: now);
    await expectLater(repo.enviarUbicacion(u),
        throwsA(isA<TooManyRequestsException>().having((e) => e.retryAfter, 'retry', const Duration(seconds: 2))));
    await expectLater(repo.enviarUbicacion(u),
        throwsA(isA<ConflictException>().having((e) => e.message, 'msg', 'No tienes un turno abierto.')));
  });
}

class SocketLikeError implements Exception {
  const SocketLikeError();
}
