import 'dart:async';

import 'package:geolocator/geolocator.dart';

/// Estado del permiso de ubicación y del servicio GPS del teléfono.
enum PermisoUbicacion {
  concedido,
  noSolicitado,
  denegado,
  denegadoPermanente,
  servicioApagado,
}

/// Una posición tal como la entrega el sistema. Nunca se fabrica.
class PosicionDispositivo {
  const PosicionDispositivo({
    required this.latitud,
    required this.longitud,
    required this.precisionMetros,
    required this.capturadaEn,
    required this.simulada,
  });
  final double latitud;
  final double longitud;
  final double precisionMetros;
  final DateTime capturadaEn;

  /// Android marca así las posiciones de apps de "ubicación simulada": no se envían.
  final bool simulada;
}

abstract interface class LocationSource {
  Future<PermisoUbicacion> estado();
  Future<PermisoUbicacion> solicitar();
  Stream<PosicionDispositivo> posiciones({required Duration intervalo});
  Future<bool> abrirAjustesApp();
  Future<bool> abrirAjustesUbicacion();
}

/// Implementación real con `geolocator` (Fused Location Provider de Android).
/// Solo primer plano: el seguimiento en segundo plano requiere un servicio con
/// notificación persistente y pruebas en teléfono real (pendiente).
class GeolocatorLocationSource implements LocationSource {
  @override
  Future<PermisoUbicacion> estado() async {
    if (!await Geolocator.isLocationServiceEnabled()) return PermisoUbicacion.servicioApagado;
    return _map(await Geolocator.checkPermission());
  }

  @override
  Future<PermisoUbicacion> solicitar() async {
    if (!await Geolocator.isLocationServiceEnabled()) return PermisoUbicacion.servicioApagado;
    var permission = await Geolocator.checkPermission();
    if (permission == LocationPermission.denied) permission = await Geolocator.requestPermission();
    return _map(permission);
  }

  PermisoUbicacion _map(LocationPermission p) => switch (p) {
        LocationPermission.always || LocationPermission.whileInUse => PermisoUbicacion.concedido,
        LocationPermission.deniedForever => PermisoUbicacion.denegadoPermanente,
        LocationPermission.denied => PermisoUbicacion.noSolicitado,
        LocationPermission.unableToDetermine => PermisoUbicacion.denegado,
      };

  @override
  Stream<PosicionDispositivo> posiciones({required Duration intervalo}) => Geolocator.getPositionStream(
        locationSettings: AndroidSettings(
          accuracy: LocationAccuracy.high,
          distanceFilter: 0,
          intervalDuration: intervalo,
        ),
      ).map((p) => PosicionDispositivo(
            latitud: p.latitude,
            longitud: p.longitude,
            precisionMetros: p.accuracy,
            capturadaEn: p.timestamp.toUtc(),
            simulada: p.isMocked,
          ));

  @override
  Future<bool> abrirAjustesApp() => Geolocator.openAppSettings();

  @override
  Future<bool> abrirAjustesUbicacion() => Geolocator.openLocationSettings();
}
