import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:radiomovil_conductor/app/theme.dart';
import 'package:radiomovil_conductor/core/api/api_client.dart';
import 'package:radiomovil_conductor/core/auth/token_store.dart';
import 'package:radiomovil_conductor/features/carrera/carrera_panel.dart';
import 'package:radiomovil_conductor/features/conductor/conductor_models.dart';
import 'package:radiomovil_conductor/features/session/driver_controller.dart';
import 'package:radiomovil_conductor/features/tracking/tracking_controller.dart';

import 'fakes.dart';

void main() {
  group('CarreraAsignada.fromJson', () {
    test('sin coordenadas no inventa un punto', () {
      final c = FakeConductorApi.carrera(conCoordenadas: false);
      expect(c.recogida, isNull);
      expect(c.objetivo, isNull);
    });

    test('coordenadas fuera de rango o no numéricas se descartan', () {
      expect(Punto.deJson(120, 10), isNull);
      expect(Punto.deJson(-33.4, 'x'), isNull);
      expect(Punto.deJson(-33.4, -70.6), const Punto(-33.4, -70.6));
    });

    test('el objetivo es la recogida si está Asignado y el destino si está EnCurso', () {
      final base = <String, dynamic>{
        'viajeId': 'v',
        'asignadoEn': '2026-11-10T15:00:00Z',
        'origen': 'A',
        'recogidaLatitud': 1,
        'recogidaLongitud': 2,
        'destinoLatitud': 3,
        'destinoLongitud': 4,
      };
      expect(CarreraAsignada.fromJson({...base, 'estado': 'Asignado'}).objetivo, const Punto(1, 2));
      expect(CarreraAsignada.fromJson({...base, 'estado': 'EnCurso'}).objetivo, const Punto(3, 4));
    });

    test('textos vacíos se tratan como ausentes', () {
      final c = CarreraAsignada.fromJson({
        'viajeId': 'v',
        'estado': 'Asignado',
        'asignadoEn': '2026-11-10T15:00:00Z',
        'origen': 'A',
        'telefonoPasajero': '  ',
        'observaciones': '',
      });
      expect(c.telefonoPasajero, isNull);
      expect(c.observaciones, isNull);
    });
  });

  group('DriverController con asignación directa', () {
    late FakeConductorApi api;
    late DriverController c;

    setUp(() {
      api = FakeConductorApi();
      final store = MemoryTokenStore()
        ..tokens = AuthTokens(
            accessToken: 'a', refreshToken: 'r', accessExpiresAt: DateTime.now().add(const Duration(minutes: 9)));
      c = DriverController(
        client: ApiClient(
            baseUrl: Uri.parse('https://api.test/'),
            tokenStore: store,
            adapter: FakeAdapter((o, _) => FakeAdapter.json(null, 204))),
        api: api,
        empresaId: 'emp',
        trackingFactory: (_) => TrackingController(api: api, location: FakeLocation(), autoTick: false),
      );
    });

    tearDown(() => c.dispose());

    test('al abrir consulta las carreras aunque no haya ninguna', () async {
      await c.restaurar();
      expect(api.llamadasCarreras, 1);
      expect(c.carrera, isNull);
      expect(c.porAnunciar, isNull);
    });

    test('carrera nueva: se anuncia una vez y se acusa la entrega una vez', () async {
      await c.restaurar();
      api.asignar(FakeConductorApi.carrera());
      await c.refrescar(silencioso: true);
      expect(c.carrera!.viajeId, 'viaje-1');
      expect(c.porAnunciar!.viajeId, 'viaje-1');
      expect(api.acuses, isEmpty, reason: 'el acuse se envía cuando la pantalla la mostró');

      c.anuncioMostrado();
      await pumpEventQueue();
      expect(api.acuses, ['viaje-1']);
      expect(c.carrera!.recibidaEn, isNotNull);

      await c.refrescar(silencioso: true);
      await pumpEventQueue();
      expect(c.porAnunciar, isNull);
      expect(api.acuses, hasLength(1));
      expect(api.llamadasCarreras, 2, reason: 'sin cambio de carreraActivaId no se vuelve a pedir');
    });

    test('una carrera ya vista en otra apertura no vuelve a sonar', () async {
      api.asignar(FakeConductorApi.carrera(recibidaEn: DateTime.utc(2026, 11, 10, 15, 2)));
      await c.restaurar();
      expect(c.carrera, isNotNull);
      expect(c.porAnunciar, isNull);
    });

    test('si el acuse falla por red se reintenta en el siguiente sondeo', () async {
      await c.restaurar();
      api.asignar(FakeConductorApi.carrera());
      await c.refrescar(silencioso: true);
      api.errorAcuse = network;
      c.anuncioMostrado();
      await pumpEventQueue();
      expect(api.acuses, isEmpty);
      await c.refrescar(silencioso: true);
      await pumpEventQueue();
      expect(api.acuses, ['viaje-1']);
    });

    test('cuando la central cierra la carrera desaparece de la app', () async {
      api.asignar(FakeConductorApi.carrera());
      await c.restaurar();
      api.asignar(null);
      await c.refrescar(silencioso: true);
      expect(c.carrera, isNull);
    });
  });

  group('CarreraSection', () {
    Future<void> pintar(WidgetTester tester, CarreraAsignada carrera) => tester.pumpWidget(MaterialApp(
          theme: RmTheme.light(),
          home: Scaffold(
            body: SingleChildScrollView(
              child: CarreraSection(carrera: carrera, destacada: true, onNavegar: () {}, onLlamar: () {}),
            ),
          ),
        ));

    testWidgets('muestra la orden sin botones de aceptar ni rechazar', (tester) async {
      await pintar(tester, FakeConductorApi.carrera());
      expect(find.text('Nueva carrera asignada'), findsOneWidget);
      expect(find.text('Av. Central 123'), findsOneWidget);
      expect(find.text('Terminal Sur'), findsOneWidget);
      expect(find.text('Ana'), findsOneWidget);
      expect(find.text('Portón azul'), findsOneWidget);
      expect(find.text('Cómo llegar a la recogida'), findsOneWidget);
      expect(find.text('Llamar al pasajero'), findsOneWidget);
      expect(find.textContaining(RegExp('aceptar|rechazar', caseSensitive: false)), findsNothing);
    });

    testWidgets('sin coordenadas ni teléfono: solo texto, sin navegar ni llamar', (tester) async {
      await pintar(tester, FakeConductorApi.carrera(conCoordenadas: false, telefono: null));
      expect(find.text('Cómo llegar a la recogida'), findsNothing);
      expect(find.text('Llamar al pasajero'), findsNothing);
      expect(find.textContaining('no marcó la recogida'), findsOneWidget);
    });

    testWidgets('cabe en 320 dp con fuente grande en tema oscuro', (tester) async {
      tester.view.physicalSize = const Size(320, 640);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      await tester.pumpWidget(MaterialApp(
        theme: RmTheme.dark(),
        home: Builder(
          builder: (context) => MediaQuery(
            data: MediaQuery.of(context).copyWith(textScaler: const TextScaler.linear(1.3)),
            child: Scaffold(
              body: SingleChildScrollView(
                child: CarreraSection(
                    carrera: FakeConductorApi.carrera(), destacada: false, onNavegar: () {}, onLlamar: () {}),
              ),
            ),
          ),
        ),
      ));
      expect(tester.takeException(), isNull);
      expect(find.text('Carrera asignada'), findsOneWidget);
    });
  });
}
