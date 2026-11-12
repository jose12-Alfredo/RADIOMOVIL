# AGENTS.md — RadioMovil

## 1. Rol del agente
Actúa como arquitecto de software senior y desarrollador experto en sistemas empresariales, concurrencia y tiempo real.

Prioriza seguridad, integridad de datos, rendimiento, mantenibilidad y escalabilidad. No improvises soluciones ni modifiques la arquitectura sin justificación.

Lee `AGENTS.md` y `MEMORY.md` antes de trabajar.

## 2. Objetivo del proyecto
Desarrollar un sistema web y una app móvil de conductores (Flutter + Dart, Android) para empresas de radiomóviles.

Los clientes llaman a una central; el operador registra la solicitud, visualiza los conductores mediante GPS y asigna el móvil más conveniente.

El sistema debe:
- Mostrar ubicación, disponibilidad y actividad de conductores.
- Registrar solicitudes simultáneas sin perder las confirmadas.
- Administrar una cola por orden de recepción.
- Recomendar conductores por disponibilidad y tiempo de llegada.
- Gestionar asignaciones directas de la central, su seguimiento y cancelaciones (el conductor no acepta ni rechaza).
- Conservar historial, auditoría y reportes.
- Recuperarse de errores, desconexiones y reinicios.

**MVP:** despacho manual asistido. No desarrollar aplicación para pasajeros ni despacho totalmente automático sin autorización.

## 3. Stack tecnológico obligatorio
- Backend: C# + ASP.NET Core 10 Web API.
- Frontend web: React + TypeScript + Tailwind CSS.
- App móvil de conductores: Flutter + Dart (decisión permanente del responsable, 2026-11-09). No usar Kotlin ni Jetpack Compose. Proyecto autorizado: `radiomovil_conductor/` en este repositorio, con sus propios AGENTS.md y MEMORY.md; consume solo los contratos HTTP de la API.
- Base de datos: PostgreSQL + PostGIS.
- Mensajería: RabbitMQ opcional y aplazado; no implementar ni aprovisionar sin nueva autorización.
- Tiempo real: SignalR.
- Notificaciones a la app Flutter: Firebase Cloud Messaging (en su fase y con autorización).
- Mapas: MapLibre.
- Persistencia: EF Core y SQL parametrizado.
- Workers: .NET BackgroundService.
- Pruebas: xUnit, Testcontainers y k6.
- Observabilidad: OpenTelemetry y logs estructurados.

No cambiar tecnologías ni agregar servicios innecesarios sin autorización.

## 4. Arquitectura
Utilizar un **monolito modular** con separación de responsabilidades:

- `Domain`: entidades, reglas e invariantes.
- `Application`: casos de uso y coordinación.
- `Infrastructure`: PostgreSQL, RabbitMQ, GPS y servicios externos.
- `Api`: endpoints, autenticación y validación.
- `Workers`: despacho, vencimientos, recuperación y eventos.
- `frontend-web`: panel administrativo responsive.
- `radiomovil_conductor/`: app de conductores Flutter + Dart (Android); consume solo los contratos HTTP publicados por la API.

PostgreSQL es la fuente de verdad. RabbitMQ (solo si se autoriza) distribuye eventos; SignalR comunica actualizaciones. Ninguno reemplaza la persistencia.

## 5. Reglas críticas del negocio
1. Toda solicitud confirmada debe guardarse antes de responder éxito.
2. Cada solicitud tendrá ID único y orden de recepción transaccional.
3. Una solicitud no puede tener dos asignaciones activas incompatibles.
4. Un conductor no puede tener dos carreras activas incompatibles.
5. Rechazos y cancelaciones del conductor no eliminan la solicitud.
6. Una cancelación del cliente detiene nuevas ofertas y conserva el historial.
7. Las ofertas vencidas no pueden aceptarse.
8. Toda operación repetida debe ser idempotente.
9. Toda transición de estado debe validarse en el backend.
10. Los procesos interrumpidos deben recuperarse o generar alertas.
11. Toda operación crítica debe registrar fecha, actor, motivo e identificadores.
12. Los datos deben aislarse por empresa y respetar permisos.
13. El GPS desactualizado nunca debe mostrarse como ubicación actual.
14. Los estados terminales no pueden reabrirse automáticamente.

Estas reglas son invariantes: no modificarlas sin autorización.

## 6. Motor de despacho
**Flujo vigente (decisión permanente del responsable, 2026-11-10) — asignación directa:**
solicitud (recogida y destino si se conoce) → cola → candidatos disponibles más cercanos con GPS reciente → la operadora/administrador elige y confirma → el backend ASIGNA (viaje `Asignado`) → notificación al conductor tras el COMMIT → seguimiento por la central → inicio/finalización.

- Solo Administrador u Operador asignan. El conductor no acepta ni rechaza; no existen esos botones en ninguna app.
- La asignación valida, en una transacción y bajo los mismos bloqueos, empresa, turno abierto, disponibilidad y exclusividad (una carrera activa por conductor y por solicitud); es idempotente y auditada.
- Nunca simular una aceptación del conductor para crear el viaje.
- «Carrera asignada» (estado persistido) ≠ «notificación recibida» (entrega). Una notificación fallida no deshace la asignación; el conductor recupera sus carreras activas al reconectarse.
- Pasos del conductor (decisión del responsable, 2026-11-10): `Asignado → EnCamino («Ir a recoger») → EnPuerta («Llegué», opcional) → EnCurso («Iniciar recorrido») → Finalizado`. Solo el conductor dueño, con turno abierto e Idempotency-Key; cada paso guarda hora de PostgreSQL y la última posición GPS aceptada (nunca inventada). Estados activos = Asignado, EnCamino, EnPuerta, EnCurso (misma lista en `Viaje.Activos`, índices únicos parciales y trigger). La central puede cancelar antes de recoger (hasta EnPuerta), no con el pasajero a bordo.
- Rutas por calles: solo servidor PROPIO (OSRM autoalojado) configurado en `Rutas:OsrmUrl`; la app nunca lo consulta directamente. Prohibido el servidor demo público o un proveedor de pago sin autorización. Sin servidor configurado, la API responde 503.
- El motor de ofertas (oferta → aceptación) permanece como código existente con sus restricciones; no se elimina sin analizar consecuencias, y queda fuera del flujo del MVP. Las reglas 5–7 aplican solo a ese motor.

Flujo histórico del motor de ofertas: solicitud → cola → búsqueda → reserva → oferta → aceptación → viaje → finalización.

- FIFO para seleccionar pendientes según orden de registro confirmado.
- Permitir procesamiento paralelo de solicitudes diferentes.
- Utilizar bloqueos transaccionales e índices únicos para reservas.
- Aplicar `FOR UPDATE SKIP LOCKED` cuando corresponda.
- Conservar Transactional Outbox; no publicar eventos mediante RabbitMQ sin autorización nueva.
- Procesar mensajes duplicados sin repetir efectos.
- Implementar Workers de despacho, expiración, recuperación y reconciliación.
- Nunca mantener transacciones abiertas esperando conductores o APIs externas.
- Si una oferta falla, cerrarla y continuar con otro conductor.
- Si no existen candidatos, mantener la solicitud pendiente de atención y alertar.
- Una cancelación durante el viaje requiere procedimiento de incidencia.

El orden de recepción es obligatorio; el procesamiento paralelo no garantiza el mismo orden de finalización.

## 7. GPS y selección
Considerar disponibilidad, turno activo, ubicación reciente y tiempo estimado de llegada.

Parámetros iniciales configurables, pendientes de validación:
- Respuesta de oferta: 20 segundos.
- GPS reciente: hasta 30 segundos.
- GPS desactualizado: 31–90 segundos.
- Más de 90 segundos: excluir de asignación automática.
- Radio inicial de búsqueda: 3 km.

El GPS debe utilizarse con permisos adecuados y durante la jornada autorizada.

## 8. Convenciones y prohibiciones
- Código limpio, tipado, modular y mantenible.
- C# con `PascalCase`; variables en `camelCase`.
- TypeScript en modo estricto; evitar `any`.
- Utilizar `async/await` y manejo explícito de errores.
- Mantener controladores delgados y lógica en sus módulos.
- Utilizar migraciones versionadas y consultas parametrizadas.
- No duplicar lógica de negocio en React ni en la app Flutter.
- No borrar físicamente solicitudes ni auditorías.
- No almacenar contraseñas, tokens o secretos en el repositorio.
- No inventar funcionalidades, resultados de pruebas ni avances.
- No introducir microservicios ni dependencias injustificadas.
- Diseñar interfaces responsive, accesibles y profesionales.

## 9. Pruebas obligatorias
Comprobar:
- 1.000 solicitudes simultáneas de prueba sin pérdida de registros confirmados.
- Peticiones repetidas sin crear solicitudes duplicadas.
- Dos operadores intentando reservar el mismo conductor.
- Rechazos, cancelaciones y aceptaciones simultáneas.
- Ofertas vencidas y respuestas tardías.
- Reinicios del backend y Workers.
- Solo si se autoriza RabbitMQ: probar desconexión y recuperación de Outbox contra un broker real.
- Recuperación de solicitudes abandonadas.
- Autorización, aislamiento empresarial y seguridad.

No declarar una funcionalidad terminada sin verificarla. Medir rendimiento real; nunca prometer capacidad ilimitada.

## 10. Flujo de trabajo del agente
**Antes:** leer AGENTS.md, MEMORY.md e inspeccionar el repositorio. Identificar objetivo, riesgos y archivos afectados.

**Durante:** implementar por módulos, respetar la arquitectura, escribir pruebas y mantener coherencia con decisiones anteriores.

**Después:** compilar, ejecutar pruebas, revisar errores, informar resultados y actualizar MEMORY.md.

Para cambios significativos, explicar primero el plan. No ejecutar migraciones destructivas sin aprobación.

Usar Conventional Commits:
- `feat(dispatch): agregar reservas`
- `fix(offers): corregir vencimiento`
- `test(concurrency): validar solicitudes simultáneas`

CI/CD debe ejecutar compilación, análisis estático y pruebas pertinentes antes de integrar cambios.

## 11. Memoria persistente — MEMORY.md
`MEMORY.md` conserva el contexto entre sesiones y tiene un máximo de **50 líneas**.

Al iniciar cada sesión:
1. Leer AGENTS.md y MEMORY.md.
2. Verificar los avances contra el código real.
3. Continuar desde el último estado comprobado.

Al finalizar una tarea relevante:
1. Actualizar el estado actual.
2. Registrar decisiones y motivos.
3. Documentar errores y aprendizajes.
4. Actualizar próximos pasos.
5. Resumir o eliminar información obsoleta.

No inventar avances ni guardar secretos. AGENTS.md define las reglas; MEMORY.md registra el progreso. Ante contradicciones, prevalecen las reglas de AGENTS.md y las instrucciones autorizadas del responsable.

## 12. Prioridad de implementación
1. Estructura del proyecto y entorno de desarrollo.
2. Dominio, entidades y estados.
3. PostgreSQL, migraciones y restricciones.
4. Registro seguro y cola de solicitudes.
5. Motor de despacho y reservas.
6. Workers, Outbox y recuperación.
7. Autenticación y autorización empresarial real (backend y Testing primero).
8. Frontend web y mapa administrativo.
9. Contratos para la app Flutter de conductores (autenticación, turnos, GPS) y la app `radiomovil_conductor/`.
10. SignalR, notificaciones, reportes, pruebas de carga y despliegue.

## Diseño de interfaces — Impeccable (obligatorio y permanente)

**Regla permanente:** todo trabajo de desarrollo, diseño, revisión o mejora visual del frontend debe realizarse cargando y aplicando la skill Impeccable. Rige para cualquier agente (Claude, Codex u otro) y para cualquier tamaño de cambio. Mencionarla sin cargar sus instrucciones no cumple esta regla.

- Ubicación: `.claude/skills/impeccable` (Claude), `.agents/skills/impeccable` (Codex), `.opencode/skills/impeccable` (OpenCode).
- Procedimiento y detalle: `frontend-web/AGENTS.md`, sección 5.
- Si la skill no está disponible, detenerse e informar al responsable; no sustituirla por criterio propio ni declararla aplicada.
- Utilizar obligatoriamente la skill Impeccable al crear, modificar o mejorar interfaces de usuario.
- Diseñar interfaces profesionales, modernas, responsive y accesibles.
- Mantener coherencia visual entre pantallas y componentes.
- Priorizar la facilidad de uso del operador de radiomóviles.
- Evitar diseños genéricos, elementos innecesarios y componentes inconsistentes.
- Aplicar auditorías y mejoras de diseño con Impeccable cuando corresponda.
- Mantener la lógica de negocio fuera de los componentes visuales.
- Respetar AGENTS.md y MEMORY.md durante cualquier rediseño.
**Regla final:** construir un sistema confiable de despacho, no solamente una interfaz visual. Nunca sacrificar integridad, concurrencia, seguridad o trazabilidad por rapidez de desarrollo.
