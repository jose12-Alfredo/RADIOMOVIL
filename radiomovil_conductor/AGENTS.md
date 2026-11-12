# AGENTS.md — radiomovil_conductor (Flutter + Dart, Android)

Leer antes: `../AGENTS.md`, `../RADIOMOVIL/AGENTS.md`, `../RADIOMOVIL/MEMORY.md`, y este `MEMORY.md`. Las reglas generales prevalecen.

## Alcance
- Flutter + Dart es la tecnología móvil definitiva. No usar Kotlin ni Jetpack Compose (salvo el `MainActivity.kt` generado).
- Este agente solo modifica `radiomovil_conductor/`. Backend, `frontend-web/` y documentación general los mantiene la sesión de backend: pedir cambios por mensaje o al responsable.
- Consumir SOLO contratos HTTP existentes (`docs/CONTRATOS.md`). Si falta uno, presentar el contrato mínimo y esperar autorización.

## Prohibiciones
- Nunca conectar con Neon ni con otra base de datos. Solo API Testing.
- Nunca `localhost`/`127.0.0.1` como API en un teléfono; nunca HTTP; nunca desactivar validación de certificados (`badCertificateCallback`, `cleartextTrafficPermitted=true`, etc.).
- Nunca inventar posiciones, conductores, pasajeros, rutas ni estados. El estado lo decide el servidor.
- No prometer seguimiento en segundo plano sin servicio en primer plano con notificación persistente y prueba en teléfono real.
- No guardar tokens fuera de `flutter_secure_storage`; no registrar tokens ni contraseñas en logs.
- Asignación directa (decisión permanente 2026-11-10): el conductor NO acepta ni rechaza carreras. Sin esos botones ni llamadas a los endpoints de ofertas.

## Reglas técnicas
- Refresh de un solo uso: una renovación a la vez (`ApiClient._refreshTokens`).
- Escrituras de turno con `Idempotency-Key`: reutilizar solo tras error de red.
- GPS: intervalo ≥5 s; respetar 409 (detener) y 429 (`Retry-After`); descartar simuladas y lecturas >60 s.
- Lógica fuera de los widgets: controladores (`ChangeNotifier`) y repositorios.

## Diseño
Impeccable obligatorio para cualquier cambio de interfaz (`../.claude/skills/impeccable`), con `PRODUCT.md` y `DESIGN.md` de este proyecto, Material 3 y la referencia `android.md`.

## Verificación antes de declarar terminado
`flutter analyze` sin problemas, `flutter test` verde, `flutter build apk` correcto y, para GPS/permisos/certificados/mapa, prueba en teléfono real informando qué se comprobó y qué no.
