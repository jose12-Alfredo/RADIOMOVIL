import 'package:flutter/material.dart';

import '../../app/widgets.dart';
import '../conductor/conductor_models.dart';

/// Orden de servicio asignada por la central. Asignación directa: no hay
/// aceptar ni rechazar; solo la información y las acciones para cumplirla.
class CarreraSection extends StatelessWidget {
  const CarreraSection({
    super.key,
    required this.carrera,
    required this.destacada,
    required this.onNavegar,
    required this.onLlamar,
  });

  final CarreraAsignada carrera;

  /// Recién llegada: se resalta el encabezado hasta que el conductor la mira.
  final bool destacada;
  final VoidCallback onNavegar;
  final VoidCallback onLlamar;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final asignada = carrera.estado == EstadoCarrera.asignado;
    final objetivo = carrera.objetivo;
    final telefono = carrera.telefonoPasajero;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _Encabezado(carrera: carrera, destacada: destacada),
        const SizedBox(height: 12),
        Container(
          padding: const EdgeInsets.fromLTRB(14, 14, 14, 14),
          decoration: BoxDecoration(color: scheme.surfaceContainerHigh, borderRadius: BorderRadius.circular(16)),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              _Parada(
                icon: Icons.trip_origin,
                color: scheme.primary,
                etiqueta: 'Recogida',
                texto: carrera.origen,
                principal: asignada,
                conector: carrera.destino != null,
              ),
              if (carrera.destino case final destino?)
                _Parada(
                  icon: Icons.place,
                  color: scheme.onSurface,
                  etiqueta: 'Destino',
                  texto: destino,
                  principal: !asignada,
                ),
              if (carrera.nombrePasajero != null || carrera.observaciones != null) ...[
                const SizedBox(height: 4),
                Divider(height: 20, color: scheme.outlineVariant),
              ],
              if (carrera.nombrePasajero case final nombre?)
                _Dato(icon: Icons.person_outline, etiqueta: 'Pasajero', texto: nombre),
              if (carrera.observaciones case final obs?) ...[
                if (carrera.nombrePasajero != null) const SizedBox(height: 10),
                _Dato(icon: Icons.sticky_note_2_outlined, etiqueta: 'Indicaciones de la central', texto: obs),
              ],
            ],
          ),
        ),
        const SizedBox(height: 14),
        if (objetivo != null)
          FilledButton.icon(
            onPressed: onNavegar,
            icon: const Icon(Icons.navigation_outlined),
            label: Text(asignada ? 'Cómo llegar a la recogida' : 'Cómo llegar al destino'),
          )
        else
          Notice(
            message: asignada
                ? 'La central no marcó la recogida en el mapa. Guíate por la dirección.'
                : 'La central no marcó el destino en el mapa. Guíate por la dirección.',
          ),
        if (telefono != null) ...[
          const SizedBox(height: 10),
          FilledButton.tonalIcon(
            onPressed: onLlamar,
            icon: const Icon(Icons.call_outlined),
            label: const Text('Llamar al pasajero'),
          ),
        ],
      ],
    );
  }
}

class _Encabezado extends StatelessWidget {
  const _Encabezado({required this.carrera, required this.destacada});
  final CarreraAsignada carrera;
  final bool destacada;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final asignada = carrera.estado == EstadoCarrera.asignado;
    final titulo = destacada ? 'Nueva carrera asignada' : (asignada ? 'Carrera asignada' : 'Viaje en curso');
    final detalle = asignada
        ? 'Asignada ${momentoTurno(carrera.asignadoEn)}'
        : 'Lleva al pasajero a destino';
    final animar = !MediaQuery.of(context).disableAnimations;
    final bg = destacada ? scheme.primary : scheme.surface;
    final fg = destacada ? scheme.onPrimary : scheme.onSurface;
    final fgSec = destacada ? scheme.onPrimary : scheme.onSurfaceVariant;

    return Semantics(
      liveRegion: true,
      label: '$titulo. ${carrera.origen}',
      child: ExcludeSemantics(
        child: AnimatedContainer(
          duration: animar ? const Duration(milliseconds: 320) : Duration.zero,
          curve: Curves.easeOutCubic,
          padding: EdgeInsets.symmetric(horizontal: destacada ? 14 : 0, vertical: destacada ? 12 : 0),
          decoration: BoxDecoration(color: bg, borderRadius: BorderRadius.circular(16)),
          child: Row(
            children: [
              Icon(destacada ? Icons.notifications_active : Icons.assignment_turned_in_outlined, color: fg, size: 28),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(titulo, style: theme.textTheme.titleLarge?.copyWith(color: fg)),
                    Text(detalle, style: theme.textTheme.bodyMedium?.copyWith(color: fgSec)),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _Parada extends StatelessWidget {
  const _Parada({
    required this.icon,
    required this.color,
    required this.etiqueta,
    required this.texto,
    required this.principal,
    this.conector = false,
  });

  final IconData icon;
  final Color color;
  final String etiqueta;
  final String texto;

  /// La parada hacia la que va ahora: texto más grande.
  final bool principal;
  final bool conector;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    return MergeSemantics(
      child: IntrinsicHeight(
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            SizedBox(
              width: 24,
              child: Column(
                children: [
                  const SizedBox(height: 2),
                  Icon(icon, size: 20, color: color),
                  if (conector)
                    Expanded(
                      child: Container(
                        width: 2,
                        margin: const EdgeInsets.symmetric(vertical: 4),
                        color: scheme.outlineVariant,
                      ),
                    ),
                ],
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Padding(
                padding: EdgeInsets.only(bottom: conector ? 14 : 0),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(etiqueta, style: theme.textTheme.labelMedium?.copyWith(color: scheme.onSurfaceVariant)),
                    Text(
                      texto,
                      style: (principal ? theme.textTheme.titleMedium : theme.textTheme.bodyLarge)
                          ?.copyWith(color: scheme.onSurface),
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Dato extends StatelessWidget {
  const _Dato({required this.icon, required this.etiqueta, required this.texto});
  final IconData icon;
  final String etiqueta;
  final String texto;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    return MergeSemantics(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(width: 24, child: Icon(icon, size: 20, color: scheme.onSurfaceVariant)),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(etiqueta, style: theme.textTheme.labelMedium?.copyWith(color: scheme.onSurfaceVariant)),
                Text(texto, style: theme.textTheme.bodyLarge?.copyWith(color: scheme.onSurface)),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
