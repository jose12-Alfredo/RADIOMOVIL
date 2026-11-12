import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:radiomovil_conductor/app/config.dart';

const empresa = '05820d20-0d1d-4174-af46-a9cb859c5f73';

void main() {
  test('acepta HTTPS en IP LAN y normaliza la ruta base', () {
    final c = AppConfig.parse(apiBaseUrl: 'https://10.0.50.72:7285', empresaId: empresa);
    expect(c.apiBaseUrl.toString(), 'https://10.0.50.72:7285/');
    expect(c.empresaId, empresa);
    expect(c.mapStyleLight, startsWith('https://'));
  });

  test('rechaza HTTP', () {
    expect(() => AppConfig.parse(apiBaseUrl: 'http://10.0.50.72:5176', empresaId: empresa),
        throwsA(isA<ConfigException>().having((e) => e.errors.join(), 'errores', contains('HTTPS'))));
  });

  test('rechaza localhost y 127.0.0.1 porque en el teléfono apuntan al propio teléfono', () {
    for (final url in ['https://localhost:7285', 'https://127.0.0.1:7285']) {
      expect(() => AppConfig.parse(apiBaseUrl: url, empresaId: empresa), throwsA(isA<ConfigException>()));
    }
  });

  group('CA de pruebas (RM_DEV_CA_PEM_B64)', () {
    String b64(String s) => base64.encode(utf8.encode(s));
    const pem = '-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----\n';
    const url = 'https://10.0.50.52:59260';

    test('sin definir no añade CA', () {
      expect(AppConfig.parse(apiBaseUrl: url, empresaId: empresa).devCaPem, isNull);
    });

    test('en debug acepta un certificado PEM público', () {
      final c = AppConfig.parse(apiBaseUrl: url, empresaId: empresa, devCaPemBase64: b64(pem));
      expect(utf8.decode(c.devCaPem!), pem);
    });

    test('en release se rechaza', () {
      expect(
          () => AppConfig.parse(
              apiBaseUrl: url, empresaId: empresa, devCaPemBase64: b64(pem), releaseBuild: true),
          throwsA(isA<ConfigException>()));
    });

    test('rechaza claves privadas y contenido que no es certificado', () {
      for (final bad in [
        b64('-----BEGIN PRIVATE KEY-----\nx\n-----END PRIVATE KEY-----'),
        b64('$pem-----BEGIN RSA PRIVATE KEY-----'),
        b64('hola'),
        'no-es-base64!!',
      ]) {
        expect(() => AppConfig.parse(apiBaseUrl: url, empresaId: empresa, devCaPemBase64: bad),
            throwsA(isA<ConfigException>()), reason: bad);
      }
    });
  });

  test('exige URL y GUID de empresa', () {
    expect(() => AppConfig.parse(apiBaseUrl: '', empresaId: 'abc'),
        throwsA(isA<ConfigException>().having((e) => e.errors.length, 'errores', 2)));
  });
}
