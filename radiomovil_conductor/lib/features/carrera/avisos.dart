import 'package:audioplayers/audioplayers.dart';
import 'package:flutter/services.dart';
import 'package:url_launcher/url_launcher.dart';
import 'package:vibration/vibration.dart';

import '../conductor/conductor_models.dart';

/// Efectos del aviso "Nueva carrera asignada" y salidas a otras apps.
/// Separado en interfaz para probar la pantalla sin plataforma.
abstract interface class AccionesCarrera {
  /// Sonido de notificación del sistema + vibración. Nunca lanza.
  Future<void> alertar();

  /// Abre la navegación hacia [destino]. Devuelve false si no hay app que la abra.
  Future<bool> navegar(Punto destino);

  /// Abre el marcador del teléfono con [telefono]. Devuelve false si no se pudo.
  Future<bool> llamar(String telefono);
}

class AccionesCarreraPlataforma implements AccionesCarrera {
  const AccionesCarreraPlataforma();

  static AudioPlayer? _player;

  /// Tono propio (assets/sonidos/nueva_carrera.wav) por el canal de
  /// notificaciones: respeta el volumen de notificaciones y el modo silencio.
  static final _contexto = AudioContext(
    android: const AudioContextAndroid(
      contentType: AndroidContentType.sonification,
      usageType: AndroidUsageType.notificationEvent,
      audioFocus: AndroidAudioFocus.gainTransientMayDuck,
    ),
  );

  @override
  Future<void> alertar() async {
    try {
      final player = _player ??= AudioPlayer()..setReleaseMode(ReleaseMode.stop);
      await player.setAudioContext(_contexto);
      await player.play(AssetSource('sonidos/nueva_carrera.wav'));
    } catch (_) {
      // Sin sonido disponible: queda la vibración y el aviso en pantalla.
    }
    try {
      if (await Vibration.hasVibrator()) {
        await Vibration.vibrate(pattern: const [0, 400, 200, 400, 200, 600]);
      } else {
        await HapticFeedback.heavyImpact();
      }
    } catch (_) {}
  }

  @override
  Future<bool> navegar(Punto p) async {
    final ll = '${p.latitud},${p.longitud}';
    // Contrato: Google Maps (navegación), luego Waze, luego Google Maps web.
    final candidatos = [
      Uri.parse('google.navigation:q=$ll'),
      Uri.parse('https://waze.com/ul?ll=$ll&navigate=yes'),
      Uri.parse('https://www.google.com/maps/dir/?api=1&destination=$ll&travelmode=driving'),
    ];
    for (final uri in candidatos) {
      try {
        if (await launchUrl(uri, mode: LaunchMode.externalNonBrowserApplication)) return true;
      } catch (_) {}
    }
    try {
      return await launchUrl(candidatos.last, mode: LaunchMode.externalApplication);
    } catch (_) {
      return false;
    }
  }

  @override
  Future<bool> llamar(String telefono) async {
    final limpio = telefono.replaceAll(RegExp(r'[^0-9+*#]'), '');
    if (limpio.isEmpty) return false;
    try {
      return await launchUrl(Uri(scheme: 'tel', path: limpio));
    } catch (_) {
      return false;
    }
  }
}
