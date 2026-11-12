import 'dart:io' show HandshakeException, TlsException;

import 'package:dio/dio.dart';

/// Errores de la API traducidos a casos que la interfaz sabe explicar.
sealed class ApiException implements Exception {
  const ApiException(this.message);
  final String message;
  @override
  String toString() => message;

  static ApiException fromDio(DioException e) {
    // Un fallo de TLS no es "sin conexión": el servidor respondió con un certificado
    // en el que el teléfono no confía.
    if (e.error is HandshakeException || e.error is TlsException) {
      return const CertificateException();
    }
    switch (e.type) {
      case DioExceptionType.connectionError:
      case DioExceptionType.connectionTimeout:
      case DioExceptionType.sendTimeout:
      case DioExceptionType.receiveTimeout:
        return const NetworkException();
      case DioExceptionType.badCertificate:
        return const CertificateException();
      case DioExceptionType.cancel:
        return const NetworkException('Solicitud cancelada.');
      default:
        break;
    }
    final response = e.response;
    if (response == null) return const NetworkException();
    final detail = _detail(response.data);
    switch (response.statusCode) {
      case 400:
        return BadRequestException(detail ?? 'La solicitud no es válida.');
      case 401:
        return const UnauthorizedException();
      case 403:
        return const ForbiddenException();
      case 404:
        return NotFoundException(detail ?? 'No se encontró el recurso.');
      case 409:
        return ConflictException(detail ?? 'La operación entra en conflicto con el estado actual.');
      case 429:
        final retry = int.tryParse(response.headers.value('retry-after') ?? '');
        return TooManyRequestsException(Duration(seconds: retry ?? 5));
      default:
        return ServerException(response.statusCode ?? 0);
    }
  }

  static String? _detail(Object? data) {
    if (data is String && data.trim().isNotEmpty && data.length < 500) return data.trim();
    if (data is Map) {
      for (final key in const ['detail', 'title']) {
        final value = data[key];
        if (value is String && value.trim().isNotEmpty) return value.trim();
      }
    }
    return null;
  }
}

class NetworkException extends ApiException {
  const NetworkException([super.message = 'Sin conexión con la central. Reintentando…']);
}

class CertificateException extends ApiException {
  const CertificateException()
      : super('El certificado del servidor no es de confianza. No se envían datos.');
}

class UnauthorizedException extends ApiException {
  const UnauthorizedException() : super('Tu sesión terminó. Vuelve a iniciar sesión.');
}

class ForbiddenException extends ApiException {
  const ForbiddenException()
      : super('Esta cuenta no tiene permiso de conductor en esta empresa.');
}

class NotFoundException extends ApiException {
  const NotFoundException(super.message);
}

class BadRequestException extends ApiException {
  const BadRequestException(super.message);
}

class ConflictException extends ApiException {
  const ConflictException(super.message);
}

class TooManyRequestsException extends ApiException {
  const TooManyRequestsException(this.retryAfter) : super('Envío demasiado frecuente.');
  final Duration retryAfter;
}

class ServerException extends ApiException {
  const ServerException(this.statusCode)
      : super('La central respondió con un error ($statusCode). Inténtalo de nuevo.');
  final int statusCode;
}

class InvalidCredentialsException extends ApiException {
  const InvalidCredentialsException()
      : super('Correo o contraseña incorrectos, o la cuenta no pertenece a esta empresa.');
}
