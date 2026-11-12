# RadioMovil — punto de entrada para agentes

Este archivo solo indica dónde están las reglas; no las sustituye. Rige para cualquier agente (Claude, Codex u otro).

Antes de trabajar, leer:
1. `RADIOMOVIL/AGENTS.md`: reglas generales e invariantes.
2. `RADIOMOVIL/MEMORY.md`: estado del proyecto.
3. Para el frontend: `frontend-web/AGENTS.md`, `frontend-web/MEMORY.md`, `frontend-web/PRODUCT.md`, `frontend-web/DESIGN.md` y `frontend-web/TECHNICAL.md`.

**Todo trabajo de frontend exige cargar y aplicar la skill Impeccable** según `frontend-web/AGENTS.md`, sección 5.

Objetivo funcional autorizado: despacho manual asistido de radiotaxis con GPS de la app de conductores → API ASP.NET Core → mapa React, sugerencia por proximidad y ASIGNACIÓN DIRECTA por la central; PostgreSQL/PostGIS es fuente de verdad. Orden de trabajo: A registro y centrales empresariales, B conductores/vehículos/turnos, C autenticación de conductores/turnos/GPS (contratos para la app móvil), D mapa/proximidad, E asignación directa, orden de servicio al conductor y seguimiento.

**Decisión permanente (2026-11-10): asignación directa.** La operadora o el administrador asigna la carrera al conductor elegido; el conductor NO acepta ni rechaza (sin botones en Flutter ni en la web). El backend asigna de forma atómica tras validar empresa, turno, disponibilidad y exclusividad; no se simula una aceptación del conductor. La notificación se emite después del COMMIT y es distinta de la asignación: el conductor recupera sus carreras activas al reconectarse. El motor de ofertas existente no se elimina sin análisis; queda fuera del flujo del MVP. Detalle en `RADIOMOVIL/AGENTS.md` §6.

**Decisión permanente (2026-11-09): la app móvil de conductores es Flutter + Dart.** No usar Kotlin ni Jetpack Compose. El responsable autorizó (2026-11-09) crear y desarrollar el proyecto Flutter `radiomovil_conductor/` dentro de este repositorio. Ese proyecto tiene sus propios AGENTS.md y MEMORY.md, consume SOLO los contratos HTTP de la API (nunca Neon) y no modifica backend, frontend-web ni la documentación general: los cambios de contrato se solicitan al responsable y los implementa quien mantiene el backend. No anticipar fases B–E sin autorización. Solo Neon Testing; no conectar a Production/Development.
