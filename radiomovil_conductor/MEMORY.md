# MEMORY.md — radiomovil_conductor
> Máximo 50 líneas. AGENTS.md de este proyecto y los generales prevalecen.

## Estado (2026-11-09) — Fase C app, primera versión
- Autorizado por el responsable: crear y desarrollar la app Flutter en `RADIOMOVIL/radiomovil_conductor/`.
- Entorno instalado: Flutter 3.47.9 (`C:\Users\jose1\dev\flutter`), OpenJDK 21 (winget; maplibre_gl exige 21; JDK 17 queda sin uso), Android SDK (`%LOCALAPPDATA%\Android\Sdk`, platform 36, build-tools 36.1.0, NDK 28.2.13676358, CMake 3.22.1, licencias). Variables de usuario JAVA_HOME/ANDROID_HOME/PATH. `flutter doctor` sin problemas.
- OneDrive bloquea `build/`: compilar desde copia `C:\Users\jose1\dev\rm-build\radiomovil_conductor` (robocopy /MIR /XD build .dart_tool .gradle). Ver README.
- Docker Desktop NO instalado (sin WSL2). `Dockerfile` (cirruslabs/flutter:3.47.9) escrito pero no ejecutado.
- Implementado con contratos existentes: login Android Bearer + refresh único, rol Conductor obligatorio, `/conductor/yo`, vehículos autorizados, abrir/cerrar turno con Idempotency-Key, GPS real en primer plano cada ≥5 s con 409/429/400, estado de conexión y última ubicación enviada, reconexión.
- UI: mapa MapLibre (OpenFreeMap positron/dark, provisional) + panel inferior, Material 3 claro/oscuro con paleta RadioMovil. PRODUCT.md/DESIGN.md propios (Impeccable init con respuestas del responsable).
- Verificado: `flutter analyze` 0 problemas; `flutter test` 25/25 (unitarias con fakes, sin API real); `flutter build apk --debug` correcto (API https://10.0.50.72:59260, empresa Testing). Certificado LAN verificado desde PC (`/conductor/yo` → 401 sin sesión).
- VERIFICADO en dispositivo real 2026-11-10 (tableta Xiaomi, Android 16): TLS con CA de pruebas, login de conductor Jorge (Testing), /conductor/yo con turno abierto (AUTOTOYOTA), permiso de ubicación, mapa MapLibre oscuro, envío GPS continuo ("hace 3–5 s", ±13 m) con estado Reciente. Cuenta admin rechazada correctamente (sin rol Conductor).
- NO verificado aún: abrir/cerrar turno desde la app, pérdida de red y reconexión, pausa en segundo plano, tema claro, teléfono de gama baja, escala de fuente grande.
- Detalle visual pendiente: el botón "Centrar" se superpone al icono de atribución (i) de MapLibre en la esquina derecha.

## Decisiones
- `flutter_ringtone_player` descartado (compila contra android-33 y rompe el build); se usa `audioplayers` con tono propio. `url_launcher` para navegación/teléfono; `vibration` (añade permiso VIBRATE).
- Estado con `ChangeNotifier` (sin Riverpod) para minimizar dependencias.
- API y empresa por `--dart-define` (RM_API_BASE_URL https sin localhost, RM_EMPRESA_ID).
- Dart NO usa las CA de usuario de Android: en debug la CA de pruebas se compila con `RM_DEV_CA_PEM_B64` (`lib/core/api/http_adapter.dart`); release solo CA del sistema. Nunca se desactiva TLS.
- Prueba real 2026-11-10 (tableta Xiaomi 25097RP43G, Android 16, adb inalámbrico): Xiaomi bloquea `adb install` → instalar el APK desde Descargas. Server LAN https://10.0.50.52:59260; el firewall bloqueaba node.exe en red Privada (lo corrigió el responsable).
- Segundo plano: pausa explícita y aviso; servicio con notificación persistente pendiente.
- Reparto con la sesión de backend ("Proyecto Baken"): esta sesión solo toca `radiomovil_conductor/`; backend y docs generales los actualiza esa sesión.

## Pendiente
- Prueba en teléfono real: instalar CA, APK debug, login con conductor de Testing, turno y envío GPS visible en la central.
- Fase E = ASIGNACIÓN DIRECTA (decisión permanente 2026-11-10): sin aceptar/rechazar. IMPLEMENTADO 2026-11-10 (autorizado por el responsable en chat): `lib/features/carrera/`, sondeo /yo 10 s, GET /carreras, aviso con tono propio + vibración, POST recibida, marcadores, Cómo llegar, Llamar. analyze 0, test 42/42, APK debug OK. NO probado aún en la tableta con una carrera real asignada desde la central. Sin aviso en segundo plano (falta FCM/servicio).
- PROBADO en tableta 2026-11-10 16:26: carrera asignada desde la central llegó a la app (tarjeta, marcador). El responsable NO quiere app externa para la ruta: pidió ruta dentro de la app y 4 pasos (Ir a recoger → Llegué → Iniciar recorrido → Finalizar). Contrato SOLICITADO a backend (docs/CONTRATOS.md); servidor de rutas propio. Esperar publicación.
- Servicio de ubicación en primer plano + permisos de segundo plano, probado en dispositivo.
- Firma release real, icono de la app, decidir proveedor de teselas definitivo.
