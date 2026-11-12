import 'package:connectivity_plus/connectivity_plus.dart';
import 'package:flutter/material.dart';

import 'app/app.dart';
import 'app/config.dart';
import 'core/api/api_client.dart';
import 'core/api/http_adapter.dart';
import 'core/auth/token_store.dart';
import 'features/conductor/conductor_repository.dart';
import 'features/session/driver_controller.dart';
import 'features/tracking/location_source.dart';
import 'features/tracking/tracking_controller.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  final AppConfig config;
  try {
    config = AppConfig.fromEnvironment();
  } on ConfigException catch (e) {
    runApp(ConfigErrorApp(error: e));
    return;
  }

  final client = ApiClient(
    baseUrl: config.apiBaseUrl,
    tokenStore: SecureTokenStore(),
    adapter: buildHttpAdapter(extraCaPem: config.devCaPem),
  );
  final api = ConductorRepository(client);
  final location = GeolocatorLocationSource();
  final controller = DriverController(
    client: client,
    api: api,
    empresaId: config.empresaId,
    trackingFactory: (driver) => TrackingController(
      api: api,
      location: location,
      onTurnoCerrado: () => driver.refrescar(silencioso: true),
    ),
  );
  runApp(RadioMovilApp(
    config: config,
    controller: controller,
    connectivity: Connectivity().onConnectivityChanged,
  ));
}
