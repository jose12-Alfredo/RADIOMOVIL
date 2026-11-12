import 'package:flutter_test/flutter_test.dart';
import 'package:radiomovil_conductor/core/api/api_exception.dart';
import 'package:radiomovil_conductor/features/conductor/conductor_models.dart';
import 'package:radiomovil_conductor/features/tracking/location_source.dart';
import 'package:radiomovil_conductor/features/tracking/tracking_controller.dart';

import 'fakes.dart';

void main() {
  late FakeConductorApi api;
  late FakeLocation location;
  late FakeClock clock;
  late TrackingController t;
  var turnoCerrado = 0;

  PosicionDispositivo pos({Duration edad = Duration.zero, bool simulada = false}) => PosicionDispositivo(
      latitud: -16.5, longitud: -68.15, precisionMetros: 7, capturadaEn: clock.now.subtract(edad), simulada: simulada);

  Future<void> emit(PosicionDispositivo p) async {
    location.emitir(p);
    await Future<void>.delayed(Duration.zero);
  }

  setUp(() async {
    api = FakeConductorApi();
    location = FakeLocation();
    clock = FakeClock(DateTime.utc(2026, 11, 9, 12));
    turnoCerrado = 0;
    t = TrackingController(api: api, location: location, clock: clock.call, autoTick: false, onTurnoCerrado: () => turnoCerrado++);
    await t.iniciar(intervaloSegundos: 5);
  });

  tearDown(() => t.dispose());

  test('envía la posición real con su precisión y hora de captura', () async {
    await emit(pos());
    await t.tick();
    expect(api.enviados, hasLength(1));
    expect(api.enviados.single.precisionMetros, 7);
    expect(api.enviados.single.capturadaEn, clock.now);
    expect(t.estadoGps, EstadoGps.reciente);
    expect(t.conexion, EstadoConexion.enLinea);
  });

  test('respeta el intervalo y no repite la misma captura', () async {
    await emit(pos());
    await t.tick();
    clock.advance(const Duration(seconds: 2));
    await emit(pos());
    await t.tick();
    expect(api.enviados, hasLength(1), reason: 'antes de 5 s no se envía');
    clock.advance(const Duration(seconds: 4));
    await t.tick();
    expect(api.enviados, hasLength(2));
    clock.advance(const Duration(seconds: 6));
    await t.tick();
    expect(api.enviados, hasLength(2), reason: 'sin lectura nueva no se reenvía');
  });

  test('clasifica reciente, desactualizado y sin señal según el último envío aceptado', () async {
    await emit(pos());
    await t.tick();
    clock.advance(const Duration(seconds: 45));
    expect(t.estadoGps, EstadoGps.desactualizado);
    clock.advance(const Duration(seconds: 60));
    expect(t.estadoGps, EstadoGps.sinSenal);
  });

  test('no envía posiciones simuladas ni viejas', () async {
    await emit(pos(simulada: true));
    await t.tick();
    expect(api.enviados, isEmpty);
    expect(t.aviso, contains('simulada'));
    clock.advance(const Duration(seconds: 10));
    await emit(pos(edad: const Duration(seconds: 90)));
    await t.tick();
    expect(api.enviados, isEmpty);
  });

  test('sin red marca desconexión, reintenta con espera y al reconectar envía solo la última', () async {
    await emit(pos());
    api.errorEnvio = network;
    await t.tick();
    expect(t.conexion, EstadoConexion.sinConexion);
    expect(api.enviados, isEmpty);
    clock.advance(const Duration(seconds: 1));
    await t.tick();
    expect(api.enviados, isEmpty, reason: 'espera antes de reintentar');
    final ultima = pos();
    await emit(ultima);
    t.conexionRecuperada();
    await Future<void>.delayed(Duration.zero);
    expect(api.enviados, hasLength(1));
    expect(api.enviados.single.capturadaEn, ultima.capturadaEn);
    expect(t.conexion, EstadoConexion.enLinea);
  });

  test('429 espera el Retry-After del servidor', () async {
    await emit(pos());
    api.errorEnvio = const TooManyRequestsException(Duration(seconds: 3));
    await t.tick();
    clock.advance(const Duration(seconds: 2));
    await t.tick();
    expect(api.enviados, isEmpty);
    clock.advance(const Duration(seconds: 1));
    await t.tick();
    expect(api.enviados, hasLength(1));
  });

  test('409 (sin turno abierto) detiene el seguimiento', () async {
    await emit(pos());
    api.errorEnvio = const ConflictException('No tienes un turno abierto.');
    await t.tick();
    expect(t.activo, isFalse);
    expect(turnoCerrado, 1);
    clock.advance(const Duration(seconds: 10));
    await emit(pos());
    await t.tick();
    expect(api.enviados, isEmpty);
  });

  test('en segundo plano se pausa y al volver se reanuda', () async {
    t.pausarPorSegundoPlano();
    await emit(pos());
    await t.tick();
    expect(api.enviados, isEmpty);
    expect(t.pausadoSegundoPlano, isTrue);
    await t.reanudar();
    await emit(pos());
    await t.tick();
    expect(api.enviados, hasLength(1));
  });

  test('sin permiso no hay lecturas ni envíos', () async {
    t.detener();
    location.permiso = PermisoUbicacion.denegadoPermanente;
    await t.iniciar(intervaloSegundos: 5);
    await emit(pos());
    await t.tick();
    expect(api.enviados, isEmpty);
    expect(t.permiso, PermisoUbicacion.denegadoPermanente);
  });
}
