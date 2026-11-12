import 'dart:async';

import 'package:connectivity_plus/connectivity_plus.dart';
import 'package:flutter/material.dart';

import '../features/home/home_screen.dart';
import '../features/session/driver_controller.dart';
import '../features/session/login_screen.dart';
import 'config.dart';
import 'theme.dart';
import 'widgets.dart';

class RadioMovilApp extends StatefulWidget {
  const RadioMovilApp({super.key, required this.config, required this.controller, this.connectivity});
  final AppConfig config;
  final DriverController controller;
  final Stream<List<ConnectivityResult>>? connectivity;

  @override
  State<RadioMovilApp> createState() => _RadioMovilAppState();
}

class _RadioMovilAppState extends State<RadioMovilApp> with WidgetsBindingObserver {
  StreamSubscription<List<ConnectivityResult>>? _net;
  bool? _online;

  DriverController get c => widget.controller;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _net = widget.connectivity?.listen((results) {
      final online = results.any((r) => r != ConnectivityResult.none);
      if (online == _online) return;
      final wasKnown = _online != null;
      _online = online;
      if (!online) {
        c.conexionPerdida();
      } else if (wasKnown) {
        c.conexionRecuperada();
      }
    });
    c.restaurar();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    switch (state) {
      case AppLifecycleState.paused:
      case AppLifecycleState.hidden:
        c.tracking.pausarPorSegundoPlano();
      case AppLifecycleState.resumed:
        c.tracking.reanudar();
        c.refrescar(silencioso: true, carreras: true);
      case AppLifecycleState.inactive:
      case AppLifecycleState.detached:
        break;
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _net?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'RadioMovil Conductor',
      debugShowCheckedModeBanner: false,
      theme: RmTheme.light(),
      darkTheme: RmTheme.dark(),
      themeMode: ThemeMode.system,
      home: ListenableBuilder(
        listenable: c,
        builder: (context, _) => switch (c.fase) {
          FaseSesion.sinSesion => LoginScreen(controller: c),
          FaseSesion.lista => HomeScreen(controller: c, config: widget.config),
          FaseSesion.iniciando || FaseSesion.cargando => _Loading(controller: c),
        },
      ),
    );
  }
}

class _Loading extends StatelessWidget {
  const _Loading({required this.controller});
  final DriverController controller;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const BrandMark(size: 64),
                const SizedBox(height: 28),
                if (controller.sinConexion) ...[
                  Notice(message: controller.error ?? 'Sin conexión con la central.', tone: NoticeTone.warn),
                  const SizedBox(height: 16),
                  OutlinedButton(onPressed: controller.refrescar, child: const Text('Reintentar')),
                ] else
                  const CircularProgressIndicator(semanticsLabel: 'Cargando'),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// Se muestra si la compilación no trae una configuración válida.
class ConfigErrorApp extends StatelessWidget {
  const ConfigErrorApp({super.key, required this.error});
  final ConfigException error;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      debugShowCheckedModeBanner: false,
      theme: RmTheme.light(),
      darkTheme: RmTheme.dark(),
      home: Scaffold(
        body: SafeArea(
          child: ListView(
            padding: const EdgeInsets.all(24),
            children: [
              const Align(alignment: Alignment.centerLeft, child: BrandMark()),
              const SizedBox(height: 24),
              Text('Configuración incompleta', style: Theme.of(context).textTheme.headlineSmall),
              const SizedBox(height: 12),
              for (final e in error.errors) ...[Notice(message: e, tone: NoticeTone.error), const SizedBox(height: 8)],
              const SizedBox(height: 8),
              const Text('Esta compilación necesita RM_API_BASE_URL (HTTPS) y RM_EMPRESA_ID. Ver README del proyecto.'),
            ],
          ),
        ),
      ),
    );
  }
}
