import 'package:flutter/material.dart';

import 'theme.dart';

/// Monograma geométrico RadioMovil (sin recursos raster).
class BrandMark extends StatelessWidget {
  const BrandMark({super.key, this.size = 48});
  final double size;

  @override
  Widget build(BuildContext context) {
    final dark = Theme.of(context).brightness == Brightness.dark;
    return Semantics(
      label: 'RadioMovil',
      child: Container(
        width: size,
        height: size,
        decoration: BoxDecoration(
          color: dark ? const Color(0xFF1F477F) : RmColors.brand,
          borderRadius: BorderRadius.circular(size * .26),
        ),
        alignment: Alignment.center,
        child: ExcludeSemantics(
          child: Text('RM',
              style: TextStyle(
                  color: Colors.white, fontWeight: FontWeight.w800, fontSize: size * .38, letterSpacing: -0.5)),
        ),
      ),
    );
  }
}

enum NoticeTone { info, warn, error }

/// Aviso en línea: icono + texto, anunciado por lectores de pantalla.
class Notice extends StatelessWidget {
  const Notice({super.key, required this.message, this.tone = NoticeTone.info, this.action});
  final String message;
  final NoticeTone tone;
  final Widget? action;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final status = context.status;
    final (bg, fg, icon) = switch (tone) {
      NoticeTone.info => (scheme.secondaryContainer, scheme.onSecondaryContainer, Icons.info_outline),
      NoticeTone.warn => (status.warnContainer, status.onWarnContainer, Icons.warning_amber_rounded),
      NoticeTone.error => (scheme.errorContainer, scheme.onErrorContainer, Icons.error_outline),
    };
    return Semantics(
      liveRegion: true,
      child: Container(
        padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
        decoration: BoxDecoration(color: bg, borderRadius: BorderRadius.circular(12)),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(icon, color: fg, size: 22),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(message, style: Theme.of(context).textTheme.bodyMedium?.copyWith(color: fg)),
                  if (action != null) ...[const SizedBox(height: 4), action!],
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// "hace 4 s", "hace 3 min", "hace 2 h".
String haceTiempo(Duration d) {
  if (d.isNegative) d = Duration.zero;
  if (d.inSeconds < 60) return 'hace ${d.inSeconds} s';
  if (d.inMinutes < 60) return 'hace ${d.inMinutes} min';
  return 'hace ${d.inHours} h';
}

String horaCorta(DateTime t) {
  final l = t.toLocal();
  return '${l.hour.toString().padLeft(2, '0')}:${l.minute.toString().padLeft(2, '0')}';
}

/// "hoy a las 13:42", "ayer a las 13:42" o "el 08/11 a las 13:42": un turno largo
/// no debe parecer de hoy.
String momentoTurno(DateTime t, {DateTime? ahora}) {
  final l = t.toLocal();
  final hoy = (ahora ?? DateTime.now()).toLocal();
  final dia = DateTime(l.year, l.month, l.day);
  final diferencia = DateTime(hoy.year, hoy.month, hoy.day).difference(dia).inDays;
  final hora = horaCorta(l);
  if (diferencia == 0) return 'hoy a las $hora';
  if (diferencia == 1) return 'ayer a las $hora';
  return 'el ${l.day.toString().padLeft(2, '0')}/${l.month.toString().padLeft(2, '0')} a las $hora';
}
