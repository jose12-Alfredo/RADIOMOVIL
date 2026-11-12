import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:radiomovil_conductor/app/app.dart';
import 'package:radiomovil_conductor/app/config.dart';
import 'package:radiomovil_conductor/core/api/api_client.dart';
import 'package:radiomovil_conductor/core/api/api_exception.dart';
import 'package:radiomovil_conductor/core/auth/token_store.dart';
import 'package:radiomovil_conductor/features/session/driver_controller.dart';
import 'package:radiomovil_conductor/features/tracking/tracking_controller.dart';

import 'fakes.dart';

void main() {
  late MemoryTokenStore store;
  late FakeConductorApi api;
  late DriverController c;

  DriverController build() {
    final client = ApiClient(
      baseUrl: Uri.parse('https://api.test/'),
      tokenStore: store,
      adapter: FakeAdapter((o, _) => FakeAdapter.json(null, 204)),
    );
    return DriverController(
      client: client,
      api: api,
      empresaId: 'emp',
      trackingFactory: (_) => TrackingController(api: api, location: FakeLocation(), autoTick: false),
    );
  }

  setUp(() {
    store = MemoryTokenStore()
      ..tokens = AuthTokens(accessToken: 'a', refreshToken: 'r', accessExpiresAt: DateTime.now().add(const Duration(minutes: 9)));
    api = FakeConductorApi();
    c = build();
  });

  test('restaura sesión de conductor y carga su perfil', () async {
    await c.restaurar();
    expect(c.fase, FaseSesion.lista);
    expect(c.yo!.nombre, 'Jorge');
    expect(c.tracking.activo, isFalse, reason: 'sin turno no se comparte ubicación');
  });

  test('una cuenta sin rol Conductor no entra', () async {
    api.roles = ['Operador'];
    await c.restaurar();
    expect(c.fase, FaseSesion.sinSesion);
    expect(c.error, const ForbiddenException().message);
    expect(store.tokens, isNull);
  });

  test('abrir turno reutiliza la Idempotency-Key si falló la red y activa el GPS', () async {
    await c.restaurar();
    api.errorAbrir = network;
    expect(await c.abrirTurno('v1'), isNotNull);
    expect(await c.abrirTurno('v1'), isNull);
    expect(api.clavesAbrir, hasLength(2));
    expect(api.clavesAbrir[0], api.clavesAbrir[1]);
    expect(c.yo!.enTurno, isTrue);
    expect(c.tracking.activo, isTrue);
  });

  test('cerrar turno usa clave nueva tras éxito y detiene el GPS', () async {
    await c.restaurar();
    await c.abrirTurno('v1');
    api.errorCerrar = const ConflictException('Tienes una oferta activa.');
    expect(await c.cerrarTurno(null), 'Tienes una oferta activa.');
    expect(await c.cerrarTurno('Fin de jornada'), isNull);
    expect(api.clavesCerrar[0], isNot(api.clavesCerrar[1]), reason: 'un 409 es definitivo: nueva intención');
    expect(c.yo!.enTurno, isFalse);
    expect(c.tracking.activo, isFalse);
  });

  testWidgets('sin sesión muestra el login y valida los campos', (tester) async {
    store.tokens = null;
    final config = AppConfig.parse(apiBaseUrl: 'https://api.test', empresaId: '05820d20-0d1d-4174-af46-a9cb859c5f73');
    await tester.pumpWidget(RadioMovilApp(config: config, controller: c));
    await tester.pumpAndSettle();
    expect(find.text('Ingreso de conductores'), findsOneWidget);
    await tester.tap(find.text('Ingresar'));
    await tester.pump();
    expect(find.text('Escribe tu correo.'), findsOneWidget);
    expect(find.text('Escribe tu contraseña.'), findsOneWidget);
    await tester.binding.setSurfaceSize(const Size(320, 568));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull, reason: 'sin desbordes en pantallas pequeñas');
    await tester.binding.setSurfaceSize(null);
  });
}
