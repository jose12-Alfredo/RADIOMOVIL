# RadioMovil Conductor (Flutter + Dart, Android)

App de conductores de RadioMovil: login empresarial, perfil y vehículo, turno propio, estado confirmado por el servidor, envío de GPS real durante el turno y la carrera que la central asigna directamente (sin aceptar ni rechazar). Consume solo la API ASP.NET Core 10 (Neon Testing). Contratos: [docs/CONTRATOS.md](docs/CONTRATOS.md).

## Entorno (Windows, instalado 2026-11-09)

- Flutter 3.47.9 stable (Dart 3.13.7) en `C:\Users\jose1\dev\flutter`.
- Microsoft OpenJDK **21** (`JAVA_HOME`, `flutter config --jdk-dir`): `maplibre_gl` exige Java 21. JDK 17 sigue instalado pero no se usa.
- Android SDK en `%LOCALAPPDATA%\Android\Sdk` (`ANDROID_HOME`): cmdline-tools, platform-tools, platform 36, build-tools 36.1.0, NDK 28.2.13676358, CMake 3.22.1; licencias aceptadas.
- `flutter doctor`: sin problemas. Docker Desktop: no instalado (requiere WSL2); ver “Docker”.

## Configuración (`--dart-define`, sin secretos)

| Variable | Obligatoria | Descripción |
|---|---|---|
| `RM_API_BASE_URL` | sí | URL **HTTPS** de la API accesible desde el teléfono (IP LAN o dominio). `localhost`/`127.0.0.1` se rechazan. |
| `RM_EMPRESA_ID` | sí | GUID de la empresa (Testing: `05820d20-0d1d-4174-af46-a9cb859c5f73`). |
| `RM_DEV_CA_PEM_B64` | no (solo debug) | Certificado **público** de la CA local de pruebas en base64. Se rechaza en release y si contiene claves. |
| `RM_MAP_STYLE_LIGHT` / `RM_MAP_STYLE_DARK` | no | Estilos MapLibre. Por defecto OpenFreeMap `positron`/`dark` (sin clave; provisional, el proveedor recibe las peticiones de teselas). |

Sin configuración válida la app muestra una pantalla de “Configuración incompleta”.

## Ejecutar y compilar

```bash
flutter pub get
flutter analyze
flutter test
flutter run --dart-define=RM_API_BASE_URL=https://10.0.50.72:59260 --dart-define=RM_EMPRESA_ID=05820d20-0d1d-4174-af46-a9cb859c5f73
flutter build apk --debug --dart-define=RM_API_BASE_URL=https://10.0.50.72:59260 --dart-define=RM_EMPRESA_ID=05820d20-0d1d-4174-af46-a9cb859c5f73
```

La IP/puerto de ejemplo es el servidor HTTPS LAN temporal que expone la sesión de backend; coordinar con ella antes de cambiar puertos.

**OneDrive:** el repositorio está dentro de OneDrive, que bloquea `build/` mientras Gradle escribe ("Unable to delete directory"). Compilar desde una copia fuera de OneDrive:

```powershell
robocopy "<repo>\radiomovil_conductor" C:\Users\jose1\dev\rm-build\radiomovil_conductor /MIR /XD build .dart_tool .gradle
cd C:\Users\jose1\dev\rm-build\radiomovil_conductor; flutter pub get; flutter build apk --debug --dart-define=...
```

El código se edita solo en el repositorio; la copia es desechable. APK debug resultante: `build\app\outputs\flutter-apk\app-debug.apk` (~185 MB, incluye todas las ABI y símbolos de depuración; un release por ABI será mucho menor).

## HTTPS en un teléfono físico

- Nunca se desactiva TLS ni la validación de certificados (`network_security_config.xml`: sin tráfico en claro).
- **Dart (dio/HttpClient) NO usa `network_security_config.xml` ni las CA instaladas por el usuario en Android**: solo las CA del sistema. Instalar la CA en el teléfono no basta (comprobado 2026-11-10: fallaba el TLS y la app decía "sin conexión").
- Para pruebas en LAN, en **debug** se compila la CA pública de pruebas como raíz adicional con `RM_DEV_CA_PEM_B64` (base64 del `.pem`). La validación sigue completa y las CA del sistema siguen activas. En release esta variable se rechaza.

  ```powershell
  $b64=[Convert]::ToBase64String([IO.File]::ReadAllBytes("$env:LOCALAPPDATA\RadioMovil\lan-certs\radiomovil-prueba-ca.pem"))
  flutter build apk --debug --dart-define=RM_API_BASE_URL=https://10.0.50.52:59260 --dart-define=RM_EMPRESA_ID=05820d20-0d1d-4174-af46-a9cb859c5f73 "--dart-define=RM_DEV_CA_PEM_B64=$b64"
  ```
- `network_security_config.xml` (sin tráfico en claro; debug confía en CA de usuario) solo afecta al código nativo (p. ej. el mapa), no a la API. Las `.key` nunca salen del PC.
- Un fallo TLS se muestra como "El certificado del servidor no es de confianza", no como "sin conexión".
- La CA local es temporal (30 días). Producción requiere un certificado público válido.

## GPS

- Permiso de ubicación precisa en primer plano, solicitado con explicación previa; se gestionan negado, negado permanente (abre Ajustes) y ubicación apagada.
- Envía `latitud, longitud, precisionMetros, capturadaEn` cada `intervaloEnvioSegundos` (≥5 s) solo con turno abierto; descarta ubicaciones simuladas y lecturas >60 s; sin red no acumula: al reconectar envía la última.
- Estado: Reciente ≤30 s, Desactualizado 31–90 s, Sin ubicación >90 s desde el último envío aceptado.
- **Segundo plano: no soportado todavía.** Al minimizar la app el envío se pausa y se avisa. Requiere servicio en primer plano con notificación persistente, `ACCESS_BACKGROUND_LOCATION`/`FOREGROUND_SERVICE_LOCATION` y pruebas en teléfono real.

## Carrera asignada (asignación directa)

- Sin FCM todavía: en primer plano se consulta `GET conductor/yo` cada 10 s; `GET conductor/carreras` al abrir, al volver a primer plano, al reconectar y cuando cambia `carreraActivaId`.
- Carrera nueva (sin `recibidaEn`): tono propio `assets/sonidos/nueva_carrera.wav` (generado para el proyecto, canal de notificaciones) + vibración + encabezado resaltado 1 min. Después se envía `POST .../recibida` (entrega, no aceptación); si falla por red se reintenta en el siguiente sondeo.
- Se muestran origen, destino, pasajero, teléfono e indicaciones solo si la central los registró. En el mapa se dibujan recogida (azul) y destino solo con coordenadas; sin ellas, solo texto (no se geocodifica).
- "Cómo llegar": Google Maps (`google.navigation:`), si no Waze, si no Google Maps web. "Llamar al pasajero" abre el marcador (`tel:`), no llama solo.
- Con carrera activa no se ofrece "Finalizar turno" (el servidor responde 409). Iniciar/finalizar viaje desde la app no existe aún.
- Con la app minimizada NO llega el aviso (sin servicio en primer plano ni FCM).

## Docker

`Dockerfile` compila de forma reproducible (análisis, pruebas y APK release) con `ghcr.io/cirruslabs/flutter:3.47.9`. Es opcional. No prueba GPS, permisos, certificados, mapa ni red real. **No se ha ejecutado**: Docker Desktop no está instalado en este PC (requiere habilitar WSL2 con permisos de administrador y reinicio).

## Arquitectura

```
lib/
  app/        configuración, tema Material 3 (RadioMovil), widgets comunes, raíz (ciclo de vida, conectividad)
  core/api    ApiClient (Dio, Bearer, refresh único), errores tipados
  core/auth   almacenamiento seguro de tokens
  features/
    conductor/  modelos y repositorio de contratos /conductor
    session/    DriverController (sesión, perfil, turno) y login
    tracking/   fuente de ubicación (geolocator) y TrackingController
    carrera/    tarjeta de carrera asignada, aviso (sonido/vibración), navegación y llamada
    home/       mapa MapLibre, panel de trabajo, hojas de turno
```

`MainActivity.kt` es el archivo generado por Flutter; no se escribe código Kotlin propio.
