import 'dart:async';

import 'package:flutter/material.dart';

import '../../app/config.dart';
import '../../app/theme.dart';
import '../../app/widgets.dart';
import '../carrera/avisos.dart';
import '../carrera/carrera_panel.dart';
import '../conductor/conductor_models.dart';
import '../session/driver_controller.dart';
import '../tracking/location_source.dart';
import '../tracking/tracking_controller.dart';
import 'driver_map.dart';
import 'shift_sheets.dart';

/// Pantalla de trabajo: mapa a pantalla completa y panel inferior con el estado
/// confirmado por el servidor y una sola acción principal.
class HomeScreen extends StatefulWidget {
  const HomeScreen({
    super.key,
    required this.controller,
    required this.config,
    this.acciones = const AccionesCarreraPlataforma(),
  });
  final DriverController controller;
  final AppConfig config;
  final AccionesCarrera acciones;

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  bool _seguir = true;
  double _panel = 280;
  final _panelKey = GlobalKey();

  /// Carrera recién anunciada: su encabezado se resalta durante un minuto.
  String? _destacadaId;
  String? _anunciandoId;
  Timer? _destacadaTimer;

  DriverController get c => widget.controller;
  TrackingController get t => widget.controller.tracking;

  @override
  void initState() {
    super.initState();
    t.comprobarPermiso();
  }

  @override
  void dispose() {
    _destacadaTimer?.cancel();
    super.dispose();
  }

  /// Anuncia UNA vez cada carrera nueva: vibración + sonido + resaltado, y
  /// luego acusa la entrega al servidor (no es una aceptación).
  void _anunciarSiCorresponde() {
    final nueva = c.porAnunciar;
    if (nueva == null || nueva.viajeId == _anunciandoId) return;
    _anunciandoId = nueva.viajeId;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      unawaited(widget.acciones.alertar());
      setState(() => _destacadaId = nueva.viajeId);
      _destacadaTimer?.cancel();
      _destacadaTimer = Timer(const Duration(minutes: 1), () {
        if (mounted) setState(() => _destacadaId = null);
      });
      c.anuncioMostrado();
    });
  }

  Future<void> _navegar() async {
    final objetivo = c.carrera?.objetivo;
    if (objetivo == null) return;
    if (!await widget.acciones.navegar(objetivo) && mounted) {
      _snack('No se encontró una app de mapas para abrir la ruta.');
    }
  }

  Future<void> _llamar() async {
    final tel = c.carrera?.telefonoPasajero;
    if (tel == null) return;
    if (!await widget.acciones.llamar(tel) && mounted) {
      _snack('No se pudo abrir el teléfono para llamar.');
    }
  }

  void _medirPanel() {
    final h = _panelKey.currentContext?.size?.height;
    if (h != null && (h - _panel).abs() > 4) setState(() => _panel = h);
  }

  void _snack(String msg) {
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(msg)));
  }

  Future<void> _iniciarTurno() async {
    final error = await showStartShiftSheet(context, c);
    if (!mounted || error == null) return;
    _snack(error);
  }

  Future<void> _finalizarTurno() async {
    final motivo = await showEndShiftDialog(context);
    if (motivo == null || !mounted) return;
    final error = await c.cerrarTurno(motivo.isEmpty ? null : motivo);
    if (!mounted) return;
    _snack(error ?? 'Turno finalizado. Se dejó de compartir tu ubicación.');
  }

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: Listenable.merge([c, t]),
      builder: (context, _) {
        WidgetsBinding.instance.addPostFrameCallback((_) => _medirPanel());
        _anunciarSiCorresponde();
        final permiso = t.permiso == PermisoUbicacion.concedido;
        final carrera = c.carrera;
        final pos = t.ultimaPosicion;
        return Scaffold(
          drawer: _ProfileDrawer(controller: c),
          body: Stack(
            children: [
              Positioned.fill(
                child: DriverMap(
                  styleLight: widget.config.mapStyleLight,
                  styleDark: widget.config.mapStyleDark,
                  permisoConcedido: permiso,
                  seguir: _seguir,
                  bottomPadding: _panel,
                  onSeguirCambia: (v) {
                    if (v != _seguir) setState(() => _seguir = v);
                  },
                  recogida: carrera?.recogida,
                  destino: carrera?.destinoPunto,
                  miPosicion: pos == null ? null : Punto(pos.latitud, pos.longitud),
                  encuadreId: carrera == null ? null : '${carrera.viajeId}:${carrera.estado.name}',
                ),
              ),
              SafeArea(
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(12, 8, 12, 0),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Builder(
                        builder: (context) => _MapButton(
                          icon: Icons.menu,
                          tooltip: 'Perfil y sesión',
                          onPressed: () => Scaffold.of(context).openDrawer(),
                        ),
                      ),
                      const SizedBox(width: 8),
                      Expanded(child: Align(alignment: Alignment.topRight, child: _ConnectionChip(controller: c))),
                    ],
                  ),
                ),
              ),
              if (permiso && !_seguir)
                Positioned(
                  right: 16,
                  // Por encima del botón de atribución (i) del mapa.
                  bottom: _panel + 56,
                  child: FloatingActionButton.small(
                    heroTag: 'centrar',
                    tooltip: 'Centrar en mi ubicación',
                    onPressed: () => setState(() => _seguir = true),
                    child: const Icon(Icons.my_location),
                  ),
                ),
              Align(
                alignment: Alignment.bottomCenter,
                child: _WorkPanel(
                  key: _panelKey,
                  controller: c,
                  destacada: carrera != null && carrera.viajeId == _destacadaId,
                  onIniciar: _iniciarTurno,
                  onFinalizar: _finalizarTurno,
                  onNavegar: _navegar,
                  onLlamar: _llamar,
                ),
              ),
            ],
          ),
        );
      },
    );
  }
}

class _MapButton extends StatelessWidget {
  const _MapButton({required this.icon, required this.tooltip, required this.onPressed});
  final IconData icon;
  final String tooltip;
  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Material(
      color: scheme.surface,
      elevation: 3,
      shape: const CircleBorder(),
      child: IconButton(
        tooltip: tooltip,
        icon: Icon(icon, color: scheme.onSurface),
        constraints: const BoxConstraints.tightFor(width: 48, height: 48),
        onPressed: onPressed,
      ),
    );
  }
}

class _ConnectionChip extends StatelessWidget {
  const _ConnectionChip({required this.controller});
  final DriverController controller;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final status = context.status;
    final offline = controller.sinConexion || controller.tracking.conexion == EstadoConexion.sinConexion;
    final (bg, fg, icon, label) = offline
        ? (scheme.errorContainer, scheme.onErrorContainer, Icons.cloud_off, 'Sin conexión')
        : (status.okContainer, status.onOkContainer, Icons.cloud_done_outlined, 'Conectado');
    return Semantics(
      liveRegion: true,
      label: offline ? 'Sin conexión con la central' : 'Conectado con la central',
      child: Material(
        color: bg,
        elevation: 3,
        borderRadius: BorderRadius.circular(24),
        child: InkWell(
          borderRadius: BorderRadius.circular(24),
          onTap: offline ? () => controller.refrescar() : null,
          child: ConstrainedBox(
            constraints: const BoxConstraints(minHeight: 48),
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 14),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Icon(icon, size: 20, color: fg),
                  const SizedBox(width: 8),
                  ExcludeSemantics(
                    child: Text(offline ? '$label · Reintentar' : label,
                        style: Theme.of(context).textTheme.labelLarge?.copyWith(color: fg, fontSize: 14)),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _WorkPanel extends StatelessWidget {
  const _WorkPanel({
    super.key,
    required this.controller,
    required this.destacada,
    required this.onIniciar,
    required this.onFinalizar,
    required this.onNavegar,
    required this.onLlamar,
  });
  final DriverController controller;
  final bool destacada;
  final VoidCallback onIniciar;
  final VoidCallback onFinalizar;
  final VoidCallback onNavegar;
  final VoidCallback onLlamar;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final yo = controller.yo;
    final t = controller.tracking;
    final carrera = controller.carrera;
    final media = MediaQuery.of(context);
    return ConstrainedBox(
      constraints: BoxConstraints(maxWidth: 560, maxHeight: media.size.height * .62),
      child: Material(
        color: scheme.surface,
        elevation: 8,
        shadowColor: Colors.black54,
        borderRadius: const BorderRadius.vertical(top: Radius.circular(24)),
        child: SingleChildScrollView(
          padding: EdgeInsets.fromLTRB(20, 12, 20, 16 + media.padding.bottom),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (carrera != null) ...[
                CarreraSection(
                  carrera: carrera,
                  destacada: destacada,
                  onNavegar: onNavegar,
                  onLlamar: onLlamar,
                ),
                const SizedBox(height: 16),
                Divider(height: 1, color: scheme.outlineVariant),
              ] else if (yo != null)
                _StatusBand(yo: yo),
              const SizedBox(height: 16),
              if (yo?.turno case final turno?) ...[
                _InfoRow(
                  icon: Icons.local_taxi_outlined,
                  title: turno.patente,
                  subtitle: 'En turno desde ${momentoTurno(turno.inicio)}',
                ),
                const SizedBox(height: 12),
                _GpsRow(tracking: t),
              ] else
                _InfoRow(
                  icon: Icons.location_off_outlined,
                  title: 'Fuera de turno',
                  subtitle: 'Tu ubicación no se comparte con la central.',
                ),
              ..._permisoAvisos(context, t, enTurno: yo?.enTurno ?? false),
              if (t.aviso != null && (yo?.enTurno ?? false)) ...[
                const SizedBox(height: 12),
                Notice(message: t.aviso!, tone: NoticeTone.warn),
              ],
              if (t.pausadoSegundoPlano) ...[
                const SizedBox(height: 12),
                const Notice(
                  message: 'Con la app en segundo plano el envío de ubicación se pausa. Mantén la app abierta durante el turno.',
                  tone: NoticeTone.warn,
                ),
              ],
              const SizedBox(height: 20),
              if (carrera != null)
                Text(
                  'Para finalizar el turno, la central debe cerrar esta carrera.',
                  textAlign: TextAlign.center,
                  style: theme.textTheme.bodyMedium?.copyWith(color: scheme.onSurfaceVariant),
                )
              else if (yo?.enTurno ?? false)
                OutlinedButton.icon(
                  onPressed: controller.operando ? null : onFinalizar,
                  style: OutlinedButton.styleFrom(
                    foregroundColor: scheme.error,
                    side: BorderSide(color: scheme.error.withValues(alpha: .6)),
                  ),
                  icon: const Icon(Icons.stop_circle_outlined),
                  label: const Text('Finalizar turno'),
                )
              else
                FilledButton.icon(
                  onPressed: controller.operando || yo == null ? null : onIniciar,
                  icon: controller.operando
                      ? const SizedBox.square(dimension: 20, child: CircularProgressIndicator(strokeWidth: 2.5))
                      : const Icon(Icons.play_arrow_rounded),
                  label: const Text('Iniciar turno'),
                ),
            ],
          ),
        ),
      ),
    );
  }

  List<Widget> _permisoAvisos(BuildContext context, TrackingController t, {required bool enTurno}) {
    final p = t.permiso;
    if (p == null || p == PermisoUbicacion.concedido) return const [];
    final (msg, label, accion) = switch (p) {
      PermisoUbicacion.servicioApagado => (
          'La ubicación del teléfono está apagada. Actívala para que la central te vea.',
          'Activar ubicación',
          t.abrirAjustes,
        ),
      PermisoUbicacion.denegadoPermanente => (
          'Negaste el permiso de ubicación. Habilítalo en Ajustes > Permisos > Ubicación.',
          'Abrir ajustes',
          t.abrirAjustes,
        ),
      _ => (
          'RadioMovil usa tu ubicación precisa solo durante el turno, para que la central te asigne carreras cercanas.',
          'Permitir ubicación',
          t.solicitarPermiso,
        ),
    };
    return [
      const SizedBox(height: 12),
      Notice(
        message: msg,
        tone: enTurno ? NoticeTone.warn : NoticeTone.info,
        action: TextButton(onPressed: () => accion(), child: Text(label)),
      ),
    ];
  }
}

/// Firma visual: la banda de estado. Color + icono + texto, legible de reojo.
class _StatusBand extends StatelessWidget {
  const _StatusBand({required this.yo});
  final ConductorPropio yo;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final status = context.status;
    final (bg, fg, icon, detalle) = switch (yo.estado) {
      EstadoConductor.disponible => (status.okContainer, status.onOkContainer, Icons.check_circle, 'Puedes recibir carreras'),
      EstadoConductor.ocupado => (
          status.warnContainer,
          status.onWarnContainer,
          Icons.directions_car_filled,
          yo.ocupacion == 'Viaje' ? 'Carrera en curso' : 'Ocupado por la central'
        ),
      EstadoConductor.fueraDeServicio => (status.offContainer, status.onOffContainer, Icons.pause_circle, 'Inicia turno para trabajar'),
    };
    return Semantics(
      liveRegion: true,
      label: '${yo.nombre}. Estado: ${yo.estado.etiqueta}. $detalle',
      child: ExcludeSemantics(
        child: Container(
          padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
          decoration: BoxDecoration(color: bg, borderRadius: BorderRadius.circular(16)),
          child: Row(
            children: [
              Icon(icon, color: fg, size: 32),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(yo.estado.etiqueta, style: theme.textTheme.titleLarge?.copyWith(color: fg)),
                    Text(detalle, style: theme.textTheme.bodyMedium?.copyWith(color: fg)),
                  ],
                ),
              ),
              Flexible(
                child: Text(yo.nombre,
                    textAlign: TextAlign.end,
                    overflow: TextOverflow.ellipsis,
                    maxLines: 2,
                    style: theme.textTheme.labelLarge?.copyWith(color: fg, fontSize: 14)),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _InfoRow extends StatelessWidget {
  const _InfoRow({required this.icon, required this.title, required this.subtitle, this.iconColor});
  final IconData icon;
  final String title;
  final String subtitle;
  final Color? iconColor;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return MergeSemantics(
      child: Row(
        children: [
          Container(
            width: 44,
            height: 44,
            decoration: BoxDecoration(color: theme.colorScheme.surfaceContainerHigh, borderRadius: BorderRadius.circular(12)),
            child: Icon(icon, color: iconColor ?? theme.colorScheme.onSurfaceVariant),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(title, style: theme.textTheme.titleMedium),
                Text(subtitle, style: theme.textTheme.bodyMedium?.copyWith(color: theme.colorScheme.onSurfaceVariant)),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _GpsRow extends StatelessWidget {
  const _GpsRow({required this.tracking});
  final TrackingController tracking;

  @override
  Widget build(BuildContext context) {
    final status = context.status;
    final scheme = Theme.of(context).colorScheme;
    final estado = tracking.estadoGps;
    final edad = tracking.edadUltimoEnvio;
    final (icon, color) = switch (estado) {
      EstadoGps.reciente => (Icons.gps_fixed, status.ok),
      EstadoGps.desactualizado => (Icons.gps_not_fixed, status.warn),
      EstadoGps.sinSenal => (Icons.gps_off, scheme.error),
    };
    final p = tracking.ultimaPosicion;
    final precision = p == null ? '' : ' · precisión ±${p.precisionMetros.round()} m';
    final subtitle = edad == null
        ? (p == null ? 'Buscando señal GPS…' : 'Aún no se envió tu ubicación')
        : 'Última ubicación enviada ${haceTiempo(edad)}$precision';
    return _InfoRow(icon: icon, iconColor: color, title: estado.etiqueta, subtitle: subtitle);
  }
}

class _ProfileDrawer extends StatelessWidget {
  const _ProfileDrawer({required this.controller});
  final DriverController controller;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final yo = controller.yo;
    return NavigationDrawer(
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(28, 24, 24, 8),
          child: Row(
            children: [
              const BrandMark(size: 44),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(yo?.nombre ?? 'Conductor', style: theme.textTheme.titleMedium),
                    if (controller.sesion?.email case final email?)
                      Text(email,
                          overflow: TextOverflow.ellipsis,
                          style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.onSurfaceVariant)),
                  ],
                ),
              ),
            ],
          ),
        ),
        const Divider(indent: 28, endIndent: 28, height: 32),
        ListTile(
          contentPadding: const EdgeInsets.symmetric(horizontal: 28),
          leading: const Icon(Icons.badge_outlined),
          title: const Text('Estado'),
          subtitle: Text(yo?.estado.etiqueta ?? '—'),
        ),
        ListTile(
          contentPadding: const EdgeInsets.symmetric(horizontal: 28),
          leading: const Icon(Icons.local_taxi_outlined),
          title: const Text('Vehículo asignado'),
          subtitle: Text(yo?.turno?.patente ?? 'Sin turno: el vehículo se asigna al iniciar turno'),
        ),
        if (controller.actualizadoEn case final at?)
          ListTile(
            contentPadding: const EdgeInsets.symmetric(horizontal: 28),
            leading: const Icon(Icons.sync),
            title: const Text('Datos actualizados'),
            subtitle: Text('a las ${horaCorta(at)}'),
            onTap: () => controller.refrescar(),
          ),
        const Divider(indent: 28, endIndent: 28, height: 32),
        ListTile(
          contentPadding: const EdgeInsets.symmetric(horizontal: 28),
          leading: const Icon(Icons.logout),
          title: const Text('Cerrar sesión'),
          enabled: !(yo?.enTurno ?? false),
          subtitle: (yo?.enTurno ?? false) ? const Text('Finaliza el turno antes de cerrar sesión') : null,
          onTap: () async {
            Navigator.of(context).pop();
            await controller.logout();
          },
        ),
      ],
    );
  }
}
