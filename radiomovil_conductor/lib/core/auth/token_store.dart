import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Credenciales de sesión Bearer emitidas por ASP.NET Core Identity.
class AuthTokens {
  const AuthTokens({
    required this.accessToken,
    required this.refreshToken,
    required this.accessExpiresAt,
  });

  final String accessToken;
  final String refreshToken;
  final DateTime accessExpiresAt;

  /// Respuesta de /auth/login y /auth/refresh: {tokenType, accessToken, expiresIn, refreshToken}.
  factory AuthTokens.fromResponse(Map<String, dynamic> json, DateTime now) {
    final access = json['accessToken'];
    final refresh = json['refreshToken'];
    final expiresIn = json['expiresIn'];
    if (access is! String || access.isEmpty || refresh is! String || refresh.isEmpty || expiresIn is! num) {
      throw const FormatException('Respuesta de autenticación incompleta.');
    }
    return AuthTokens(
      accessToken: access,
      refreshToken: refresh,
      accessExpiresAt: now.add(Duration(seconds: expiresIn.toInt())),
    );
  }

  bool expiresSoon(DateTime now) => !now.isBefore(accessExpiresAt.subtract(const Duration(seconds: 30)));
}

abstract interface class TokenStore {
  Future<AuthTokens?> read();
  Future<void> write(AuthTokens tokens);
  Future<void> clear();
}

/// Almacenamiento cifrado del sistema (Android Keystore). Nunca SharedPreferences.
class SecureTokenStore implements TokenStore {
  SecureTokenStore([FlutterSecureStorage? storage]) : _storage = storage ?? const FlutterSecureStorage();

  final FlutterSecureStorage _storage;
  static const _access = 'rm.access';
  static const _refresh = 'rm.refresh';
  static const _expires = 'rm.access_expires';

  @override
  Future<AuthTokens?> read() async {
    final values = await _storage.readAll();
    final access = values[_access];
    final refresh = values[_refresh];
    final expires = DateTime.tryParse(values[_expires] ?? '');
    if (access == null || refresh == null || expires == null) return null;
    return AuthTokens(accessToken: access, refreshToken: refresh, accessExpiresAt: expires);
  }

  @override
  Future<void> write(AuthTokens tokens) async {
    await _storage.write(key: _access, value: tokens.accessToken);
    await _storage.write(key: _refresh, value: tokens.refreshToken);
    await _storage.write(key: _expires, value: tokens.accessExpiresAt.toUtc().toIso8601String());
  }

  @override
  Future<void> clear() async {
    await _storage.delete(key: _access);
    await _storage.delete(key: _refresh);
    await _storage.delete(key: _expires);
  }
}

class MemoryTokenStore implements TokenStore {
  AuthTokens? tokens;
  @override
  Future<AuthTokens?> read() async => tokens;
  @override
  Future<void> write(AuthTokens value) async => tokens = value;
  @override
  Future<void> clear() async => tokens = null;
}
