import 'package:flutter_test/flutter_test.dart';
import 'package:radiomovil_conductor/app/widgets.dart';

void main() {
  final ahora = DateTime(2026, 11, 10, 11, 8);

  test('turno de hoy, de ayer y de días anteriores', () {
    expect(momentoTurno(DateTime(2026, 11, 10, 7, 5), ahora: ahora), 'hoy a las 07:05');
    expect(momentoTurno(DateTime(2026, 11, 9, 13, 42), ahora: ahora), 'ayer a las 13:42');
    expect(momentoTurno(DateTime(2026, 11, 8, 23, 59), ahora: ahora), 'el 08/11 a las 23:59');
  });
}
