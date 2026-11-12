import 'dart:convert';

import 'package:flutter/foundation.dart' show kReleaseMode;

/// Configuración de compilación. Se inyecta con `--dart-define` (ver README):
///   RM_API_BASE_URL  URL HTTPS de la API ASP.NET Core accesible desde el teléfono.
///   RM_EMPRESA_ID    GUID de la empresa del conductor.
///   RM_MAP_STYLE_LIGHT / RM_MAP_STYLE_DARK  estilos MapLibre (opcionales).
///   RM_DEV_CA_PEM_B64  certificado PÚBLICO de la CA local de pruebas, en base64 (solo debug).
/// Nunca contiene secretos: solo direcciones, identificadores y certificados públicos.
class AppConfig {
  const AppConfig({
    required this.apiBaseUrl,
    required this.empresaId,
    required this.mapStyleLight,
    required this.mapStyleDark,
    this.devCaPem,
  });

  final Uri apiBaseUrl;
  final String empresaId;
  final String mapStyleLight;
  final String mapStyleDark;

  /// CA adicional de confianza para pruebas en LAN. Dart no usa las CA instaladas por
  /// el usuario en Android, así que en debug se añade explícitamente; las CA del
  /// sistema siguen activas y la validación de certificados nunca se desactiva.
  final List<int>? devCaPem;

  static const _defaultLight = 'https://tiles.openfreemap.org/styles/positron';
  static const _defaultDark = 'https://tiles.openfreemap.org/styles/dark';

  static final _guid = RegExp(
      r'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$');

  /// Hosts que en un teléfono físico apuntan al propio teléfono, no al servidor.
  static const _loopbackHosts = {'localhost', '127.0.0.1', '::1', '[::1]', '0.0.0.0'};

  static AppConfig fromEnvironment() => parse(
        apiBaseUrl: const String.fromEnvironment('RM_API_BASE_URL'),
        empresaId: const String.fromEnvironment('RM_EMPRESA_ID'),
        mapStyleLight: const String.fromEnvironment('RM_MAP_STYLE_LIGHT'),
        mapStyleDark: const String.fromEnvironment('RM_MAP_STYLE_DARK'),
        devCaPemBase64: const String.fromEnvironment('RM_DEV_CA_PEM_B64'),
        releaseBuild: kReleaseMode,
      );

  /// Valida la configuración y lanza [ConfigException] con un mensaje claro.
  static AppConfig parse({
    required String apiBaseUrl,
    required String empresaId,
    String mapStyleLight = '',
    String mapStyleDark = '',
    String devCaPemBase64 = '',
    bool releaseBuild = false,
  }) {
    final errors = <String>[];
    List<int>? devCa;
    if (devCaPemBase64.trim().isNotEmpty) {
      if (releaseBuild) {
        errors.add('RM_DEV_CA_PEM_B64 solo se admite en compilaciones debug.');
      } else {
        try {
          final bytes = base64.decode(devCaPemBase64.trim());
          final text = utf8.decode(bytes);
          if (!text.contains('-----BEGIN CERTIFICATE-----') || text.contains('PRIVATE KEY')) {
            errors.add('RM_DEV_CA_PEM_B64 debe ser solo un certificado PEM público.');
          } else {
            devCa = bytes;
          }
        } on FormatException {
          errors.add('RM_DEV_CA_PEM_B64 no es base64 válido.');
        }
      }
    }
    final uri = Uri.tryParse(apiBaseUrl.trim());
    if (apiBaseUrl.trim().isEmpty || uri == null || !uri.hasAuthority) {
      errors.add('RM_API_BASE_URL no está definida o no es una URL válida.');
    } else {
      if (uri.scheme != 'https') errors.add('RM_API_BASE_URL debe usar HTTPS.');
      if (_loopbackHosts.contains(uri.host.toLowerCase())) {
        errors.add('RM_API_BASE_URL no puede ser localhost: usa la IP LAN o el dominio del servidor.');
      }
    }
    if (!_guid.hasMatch(empresaId.trim())) {
      errors.add('RM_EMPRESA_ID debe ser el GUID de la empresa.');
    }
    if (errors.isNotEmpty) throw ConfigException(errors);
    final normalized = uri!.replace(path: uri.path.endsWith('/') ? uri.path : '${uri.path}/');
    return AppConfig(
      apiBaseUrl: normalized,
      empresaId: empresaId.trim().toLowerCase(),
      mapStyleLight: mapStyleLight.trim().isEmpty ? _defaultLight : mapStyleLight.trim(),
      mapStyleDark: mapStyleDark.trim().isEmpty ? _defaultDark : mapStyleDark.trim(),
      devCaPem: devCa,
    );
  }
}

class ConfigException implements Exception {
  ConfigException(this.errors);
  final List<String> errors;
  @override
  String toString() => errors.join('\n');
}
