import 'package:flutter/material.dart';

import '../../app/widgets.dart';
import '../../core/api/api_exception.dart';
import '../conductor/conductor_models.dart';
import '../session/driver_controller.dart';

/// Hoja inferior para elegir uno de los vehículos autorizados por la central.
/// Devuelve un mensaje de error, o null si se abrió el turno o se canceló.
Future<String?> showStartShiftSheet(BuildContext context, DriverController controller) {
  return showModalBottomSheet<String?>(
    context: context,
    isScrollControlled: true,
    useSafeArea: true,
    builder: (_) => _StartShiftSheet(controller: controller),
  );
}

class _StartShiftSheet extends StatefulWidget {
  const _StartShiftSheet({required this.controller});
  final DriverController controller;

  @override
  State<_StartShiftSheet> createState() => _StartShiftSheetState();
}

class _StartShiftSheetState extends State<_StartShiftSheet> {
  late Future<List<VehiculoAutorizado>> _vehiculos = widget.controller.vehiculos();
  String? _elegido;
  String? _error;
  bool _enviando = false;

  Future<void> _confirmar() async {
    final id = _elegido;
    if (id == null) return;
    setState(() {
      _enviando = true;
      _error = null;
    });
    final error = await widget.controller.abrirTurno(id);
    if (!mounted) return;
    if (error == null) {
      Navigator.of(context).pop();
    } else {
      setState(() {
        _enviando = false;
        _error = error;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: EdgeInsets.fromLTRB(20, 0, 20, 20 + MediaQuery.of(context).viewInsets.bottom),
      child: FutureBuilder<List<VehiculoAutorizado>>(
        future: _vehiculos,
        builder: (context, snap) {
          final children = <Widget>[
            Text('Elige tu vehículo', style: theme.textTheme.titleLarge),
            const SizedBox(height: 4),
            Text('Solo aparecen los vehículos que la central te autorizó.',
                style: theme.textTheme.bodyMedium?.copyWith(color: theme.colorScheme.onSurfaceVariant)),
            const SizedBox(height: 16),
          ];
          if (snap.connectionState != ConnectionState.done) {
            children.add(const Padding(padding: EdgeInsets.all(24), child: Center(child: CircularProgressIndicator())));
          } else if (snap.hasError) {
            final e = snap.error;
            children.addAll([
              Notice(message: e is ApiException ? e.message : 'No se pudieron cargar los vehículos.', tone: NoticeTone.error),
              const SizedBox(height: 12),
              OutlinedButton(
                onPressed: () => setState(() => _vehiculos = widget.controller.vehiculos()),
                child: const Text('Reintentar'),
              ),
            ]);
          } else if (snap.data!.isEmpty) {
            children.add(const Notice(
                message: 'No tienes vehículos autorizados. Pide a la central que te asigne uno.', tone: NoticeTone.warn));
          } else {
            children.add(RadioGroup<String>(
              groupValue: _elegido,
              onChanged: (v) => setState(() => _elegido = v),
              child: Column(
                children: [
                  for (final v in snap.data!)
                    RadioListTile<String>(
                      value: v.vehiculoId,
                      enabled: v.libre && !_enviando,
                      contentPadding: EdgeInsets.zero,
                      title: Text(v.patente, style: theme.textTheme.titleMedium),
                      subtitle: Text(v.libre ? 'Libre' : 'En uso por otro turno'),
                      secondary: const Icon(Icons.local_taxi_outlined),
                    ),
                ],
              ),
            ));
            if (_error != null) children.addAll([const SizedBox(height: 8), Notice(message: _error!, tone: NoticeTone.error)]);
            children.addAll([
              const SizedBox(height: 16),
              FilledButton(
                onPressed: _elegido == null || _enviando ? null : _confirmar,
                child: _enviando
                    ? const SizedBox.square(dimension: 22, child: CircularProgressIndicator(strokeWidth: 2.5))
                    : const Text('Iniciar turno'),
              ),
            ]);
          }
          return SingleChildScrollView(
            child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, mainAxisSize: MainAxisSize.min, children: children),
          );
        },
      ),
    );
  }
}

/// Confirmación para finalizar turno. Devuelve el motivo ('' si no se indicó) o null si se canceló.
Future<String?> showEndShiftDialog(BuildContext context) {
  return showDialog<String>(context: context, builder: (_) => const _EndShiftDialog());
}

class _EndShiftDialog extends StatefulWidget {
  const _EndShiftDialog();
  @override
  State<_EndShiftDialog> createState() => _EndShiftDialogState();
}

class _EndShiftDialogState extends State<_EndShiftDialog> {
  final _motivo = TextEditingController();

  @override
  void dispose() {
    _motivo.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text('¿Finalizar turno?'),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text('Dejarás de recibir carreras y de compartir tu ubicación.'),
          const SizedBox(height: 16),
          TextField(
            controller: _motivo,
            maxLength: 500,
            maxLines: 2,
            minLines: 1,
            decoration: const InputDecoration(labelText: 'Motivo (opcional)'),
          ),
        ],
      ),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context), child: const Text('Seguir en turno')),
        TextButton(onPressed: () => Navigator.pop(context, _motivo.text.trim()), child: const Text('Finalizar')),
      ],
    );
  }
}
