import 'dart:math' show Point, max, min;

import 'package:flutter/material.dart';
import 'package:maplibre_gl/maplibre_gl.dart';

import '../conductor/conductor_models.dart';

/// Mapa MapLibre a pantalla completa. Muestra la posición real del teléfono
/// (indicador nativo de MapLibre) y, si hay carrera, SOLO los puntos que marcó
/// la operadora (recogida/destino). Sin permiso, se ve el mundo completo: nunca
/// una ubicación inventada. Sin coordenadas no se dibuja nada (no se geocodifica).
class DriverMap extends StatefulWidget {
  const DriverMap({
    super.key,
    required this.styleLight,
    required this.styleDark,
    required this.permisoConcedido,
    required this.seguir,
    required this.onSeguirCambia,
    this.bottomPadding = 0,
    this.recogida,
    this.destino,
    this.miPosicion,
    this.encuadreId,
  });

  final String styleLight;
  final String styleDark;
  final bool permisoConcedido;
  final bool seguir;
  final ValueChanged<bool> onSeguirCambia;

  /// Altura ocupada por el panel inferior, para no tapar el indicador.
  final double bottomPadding;

  final Punto? recogida;
  final Punto? destino;
  final Punto? miPosicion;

  /// Al cambiar (p. ej. id del viaje + estado), encuadra el objetivo una vez.
  final String? encuadreId;

  @override
  State<DriverMap> createState() => _DriverMapState();
}

class _DriverMapState extends State<DriverMap> {
  MapLibreMapController? _map;
  bool _estiloListo = false;

  @override
  void didUpdateWidget(covariant DriverMap old) {
    super.didUpdateWidget(old);
    if (widget.seguir && !old.seguir) {
      _map?.animateCamera(CameraUpdate.zoomTo(16));
    }
    // Tras el frame: el encuadre cambia el estado del padre (deja de seguir).
    final redibujar = widget.recogida != old.recogida || widget.destino != old.destino;
    final encuadrar = widget.encuadreId != old.encuadreId;
    if (redibujar || encuadrar) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (!mounted) return;
        if (redibujar) _dibujarPuntos();
        if (encuadrar) _encuadrar();
      });
    }
  }

  static String _hex(Color c) => '#${(c.toARGB32() & 0xFFFFFF).toRadixString(16).padLeft(6, '0')}';

  Future<void> _dibujarPuntos() async {
    final map = _map;
    if (map == null || !_estiloListo || !mounted) return;
    final scheme = Theme.of(context).colorScheme;
    try {
      await map.clearCircles();
      if (widget.destino case final d?) {
        await map.addCircle(CircleOptions(
          geometry: LatLng(d.latitud, d.longitud),
          circleRadius: 9,
          circleColor: _hex(scheme.onSurface),
          circleStrokeColor: _hex(scheme.surface),
          circleStrokeWidth: 3,
        ));
      }
      if (widget.recogida case final r?) {
        await map.addCircle(CircleOptions(
          geometry: LatLng(r.latitud, r.longitud),
          circleRadius: 11,
          circleColor: _hex(scheme.primary),
          circleStrokeColor: _hex(scheme.surface),
          circleStrokeWidth: 3,
        ));
      }
    } catch (_) {
      // El estilo se está recargando (cambio de tema); se redibuja al cargar.
    }
  }

  Future<void> _encuadrar() async {
    final map = _map;
    if (map == null || !_estiloListo || widget.encuadreId == null) return;
    final puntos = [widget.recogida, widget.destino, widget.miPosicion].whereType<Punto>().toList();
    if (puntos.isEmpty) return;
    widget.onSeguirCambia(false);
    if (puntos.length == 1) {
      await map.animateCamera(CameraUpdate.newLatLngZoom(LatLng(puntos.first.latitud, puntos.first.longitud), 15));
      return;
    }
    var s = puntos.first.latitud, n = s, w = puntos.first.longitud, e = w;
    for (final p in puntos) {
      s = min(s, p.latitud);
      n = max(n, p.latitud);
      w = min(w, p.longitud);
      e = max(e, p.longitud);
    }
    await map.animateCamera(CameraUpdate.newLatLngBounds(
      LatLngBounds(southwest: LatLng(s, w), northeast: LatLng(n, e)),
      left: 56,
      right: 56,
      top: 120,
      bottom: widget.bottomPadding + 48,
    ));
  }

  @override
  Widget build(BuildContext context) {
    final dark = Theme.of(context).brightness == Brightness.dark;
    final tracking = widget.permisoConcedido && widget.seguir
        ? MyLocationTrackingMode.tracking
        : MyLocationTrackingMode.none;
    final hayPuntos = widget.recogida != null || widget.destino != null;
    return Semantics(
      label: [
        widget.permisoConcedido ? 'Mapa con tu ubicación' : 'Mapa sin ubicación: falta permiso',
        if (hayPuntos) 'y los puntos de la carrera',
      ].join(' '),
      child: MapLibreMap(
        key: ValueKey(dark),
        styleString: dark ? widget.styleDark : widget.styleLight,
        initialCameraPosition: const CameraPosition(target: LatLng(0, 0), zoom: 1),
        myLocationEnabled: widget.permisoConcedido,
        myLocationTrackingMode: tracking,
        compassEnabled: false,
        rotateGesturesEnabled: false,
        tiltGesturesEnabled: false,
        logoViewMargins: Point(12, widget.bottomPadding + 12),
        attributionButtonPosition: AttributionButtonPosition.bottomRight,
        attributionButtonMargins: Point(12, widget.bottomPadding + 12),
        onMapCreated: (c) {
          _map = c;
          _estiloListo = false;
        },
        onUserLocationUpdated: (_) {},
        onCameraTrackingDismissed: () => widget.onSeguirCambia(false),
        onStyleLoadedCallback: () {
          _estiloListo = true;
          _dibujarPuntos();
          if (widget.encuadreId != null && hayPuntos) {
            _encuadrar();
          } else if (widget.permisoConcedido && widget.seguir) {
            _map?.animateCamera(CameraUpdate.zoomTo(16));
          }
        },
      ),
    );
  }
}
