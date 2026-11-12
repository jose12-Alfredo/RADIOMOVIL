import 'dart:io';

import 'package:dio/dio.dart';
import 'package:dio/io.dart';

/// Adaptador HTTP con validación TLS completa.
///
/// Dart en Android NO usa `network_security_config.xml` ni las CA instaladas por el
/// usuario: solo las CA del sistema. Para probar contra la CA local de pruebas, en
/// debug se añade [extraCaPem] como raíz de confianza ADICIONAL. Nunca se usa
/// `badCertificateCallback` ni se acepta un certificado no verificado.
HttpClientAdapter buildHttpAdapter({List<int>? extraCaPem}) {
  return IOHttpClientAdapter(createHttpClient: () {
    final context = SecurityContext(withTrustedRoots: true);
    if (extraCaPem != null) context.setTrustedCertificatesBytes(extraCaPem);
    return HttpClient(context: context);
  });
}
