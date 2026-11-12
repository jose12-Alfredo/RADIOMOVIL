import 'dart:async';

import 'package:dio/dio.dart';

import '../auth/token_store.dart';
import 'api_exception.dart';

/// Cliente HTTP de la API RadioMovil (ASP.NET Core 10).
///
/// - Login y refresh van por un cliente sin credenciales.
/// - El resto lleva `Authorization: Bearer`. El refresh es de UN SOLO USO en el
///   servidor, por eso se ejecuta como máximo una renovación a la vez y todas las
///   peticiones concurrentes esperan esa misma renovación.
/// - Un 401 al renovar cierra la sesión; un fallo de red la conserva.
class ApiClient {
  ApiClient({
    required Uri baseUrl,
    required TokenStore tokenStore,
    HttpClientAdapter? adapter,
    DateTime Function()? clock,
  })  : _store = tokenStore,
        _now = clock ?? DateTime.now {
    final options = BaseOptions(
      baseUrl: baseUrl.toString(),
      connectTimeout: const Duration(seconds: 10),
      sendTimeout: const Duration(seconds: 15),
      receiveTimeout: const Duration(seconds: 15),
      contentType: Headers.jsonContentType,
      responseType: ResponseType.json,
    );
    _raw = Dio(options);
    http = Dio(options);
    if (adapter != null) {
      _raw.httpClientAdapter = adapter;
      http.httpClientAdapter = adapter;
    }
    http.interceptors.add(InterceptorsWrapper(onRequest: _onRequest, onError: _onError));
  }

  final TokenStore _store;
  final DateTime Function() _now;
  late final Dio _raw;

  /// Cliente autenticado para los repositorios.
  late final Dio http;

  final _sessionExpired = StreamController<void>.broadcast();

  /// Emite cuando el servidor rechaza la sesión (revocada, expirada o permisos retirados).
  Stream<void> get sessionExpired => _sessionExpired.stream;

  Future<AuthTokens>? _refreshing;

  Future<bool> hasSession() async => await _store.read() != null;

  Future<void> login({required String email, required String password, required String empresaId}) async {
    try {
      final response = await _raw.post<Map<String, dynamic>>('api/v1/auth/login', data: {
        'email': email,
        'password': password,
        'empresaId': empresaId,
        'cliente': 'Android',
      });
      await _store.write(AuthTokens.fromResponse(response.data ?? const {}, _now()));
    } on DioException catch (e) {
      final error = ApiException.fromDio(e);
      if (error is UnauthorizedException) throw const InvalidCredentialsException();
      throw error;
    }
  }

  /// Revoca la sesión en el servidor si hay red; las credenciales locales se borran siempre.
  Future<void> logout() async {
    try {
      await http.post<void>('api/v1/auth/logout');
    } on DioException {
      // Sin red o sesión ya revocada: igualmente se eliminan las credenciales del teléfono.
    } finally {
      await _store.clear();
    }
  }

  Future<AuthTokens> _refreshTokens() => _refreshing ??= _doRefresh().whenComplete(() => _refreshing = null);

  Future<AuthTokens> _doRefresh() async {
    final current = await _store.read();
    if (current == null) throw const UnauthorizedException();
    try {
      final response = await _raw.post<Map<String, dynamic>>('api/v1/auth/refresh',
          data: {'refreshToken': current.refreshToken});
      final tokens = AuthTokens.fromResponse(response.data ?? const {}, _now());
      await _store.write(tokens);
      return tokens;
    } on DioException catch (e) {
      final error = ApiException.fromDio(e);
      if (error is UnauthorizedException || error is ForbiddenException) await _expire();
      throw error;
    }
  }

  Future<void> _expire() async {
    await _store.clear();
    _sessionExpired.add(null);
  }

  Future<void> _onRequest(RequestOptions options, RequestInterceptorHandler handler) async {
    try {
      var tokens = await _store.read();
      if (tokens == null) {
        return handler.reject(_asDio(options, const UnauthorizedException()));
      }
      if (tokens.expiresSoon(_now())) tokens = await _refreshTokens();
      options.headers['Authorization'] = 'Bearer ${tokens.accessToken}';
      handler.next(options);
    } on ApiException catch (e) {
      handler.reject(_asDio(options, e));
    }
  }

  Future<void> _onError(DioException error, ErrorInterceptorHandler handler) async {
    final options = error.requestOptions;
    if (error.response?.statusCode != 401 || options.extra['rm.retried'] == true) {
      if (error.response?.statusCode == 401) await _expire();
      return handler.next(error);
    }
    try {
      final used = (options.headers['Authorization'] as String?)?.substring('Bearer '.length);
      final current = await _store.read();
      // Si otra petición ya renovó, se reintenta con el token nuevo sin gastar otro refresh.
      final tokens = current != null && current.accessToken != used ? current : await _refreshTokens();
      options.extra['rm.retried'] = true;
      options.headers['Authorization'] = 'Bearer ${tokens.accessToken}';
      handler.resolve(await http.fetch<dynamic>(options));
    } on ApiException catch (e) {
      handler.next(_asDio(options, e));
    } on DioException catch (e) {
      handler.next(e);
    }
  }

  DioException _asDio(RequestOptions options, ApiException error) =>
      DioException(requestOptions: options, error: error, type: DioExceptionType.unknown);

  void dispose() {
    _sessionExpired.close();
    _raw.close();
    http.close();
  }
}

/// Traduce cualquier error de Dio (incluidos los generados por el interceptor).
ApiException toApiException(Object error) {
  if (error is ApiException) return error;
  if (error is DioException) {
    final inner = error.error;
    if (inner is ApiException) return inner;
    return ApiException.fromDio(error);
  }
  return const ServerException(0);
}

