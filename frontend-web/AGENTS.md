# AGENTS.md — RadioMovil Frontend Web

## 1. Rol del agente
Actúa como arquitecto frontend senior, especialista en React, TypeScript, UX/UI y aplicaciones empresariales.

Desarrolla código profesional, seguro, mantenible, accesible y escalable. Prioriza la experiencia de los operadores y la facilidad de uso.

Antes de programar, lee:
- `../RADIOMOVIL/AGENTS.md` — Arquitectura y reglas generales.
- `../RADIOMOVIL/MEMORY.md` — Estado real del proyecto.
- Este `AGENTS.md` — Reglas específicas del frontend.

Las reglas generales del backend y sus invariantes tienen prioridad. No las contradigas.

## 2. Objetivo del frontend
Construir la plataforma web de RadioMovil para administradores y operadores.

Funciones previstas:
- Autenticación empresarial.
- Dashboard con indicadores reales.
- Gestión de solicitudes y cola de carreras.
- Despacho manual asistido.
- Visualización GPS de conductores.
- Estados de disponibilidad y turnos.
- Historial, incidencias y reportes.
- Administración de usuarios según permisos.

La app móvil de conductores es Flutter + Dart (decisión permanente, 2026-11-09), vive en `../radiomovil_conductor/` y no debe implementarse aquí. Las herramientas web temporales de prueba para conductores (por ejemplo, emisor GPS desde el navegador del teléfono) se permiten solo con autorización, siguen Impeccable y no sustituyen a la app.

## 3. Stack tecnológico
- React + TypeScript estricto.
- Vite.
- Tailwind CSS para estilos.
- Oxlint para análisis estático.
- React Router para navegación, cuando se incorpore.
- MapLibre para mapas, cuando corresponda.
- API REST de ASP.NET Core 10.
- SignalR para eventos en tiempo real, en su fase correspondiente.

No agregar bibliotecas sin justificar su necesidad. Verificar qué dependencias están instaladas antes de utilizarlas.

## 4. Arquitectura frontend
Organizar el proyecto por funcionalidades:

src/
- app/ — Configuración, navegación y proveedores.
- features/auth/ — Autenticación.
- features/dashboard/ — Panel principal.
- features/requests/ — Solicitudes.
- features/dispatch/ — Despacho.
- features/drivers/ — Conductores.
- features/tracking/ — Ubicaciones GPS.
- features/reports/ — Reportes.
- shared/components/ — Componentes reutilizables.
- shared/hooks/ — Hooks compartidos.
- shared/types/ — Tipos generales.
- services/ — API HTTP y comunicación externa.

Crear solamente las carpetas necesarias según avance el desarrollo.

Mantener separados los componentes visuales, la comunicación HTTP y el estado de la aplicación.

## 5. Diseño — Skill Impeccable (obligatoria y permanente)
**Regla inequívoca:** todo desarrollo, diseño, revisión o mejora visual de este frontend se realiza cargando y aplicando la skill Impeccable, desde la planificación hasta la revisión final de cada pantalla. Rige para cualquier agente (Claude, Codex u otro) y para cualquier tamaño de cambio. Mencionarla sin cargar sus instrucciones no cumple esta regla.

Ubicación de la skill, desde la raíz del repositorio:
- Claude: `.claude/skills/impeccable/SKILL.md`
- Codex: `.agents/skills/impeccable/SKILL.md`
- OpenCode: `.opencode/skills/impeccable/SKILL.md`

Procedimiento obligatorio en cada tarea de interfaz:
1. Cargar `SKILL.md` y seguir su preparación, incluida la carga de contexto y la lectura de `PRODUCT.md`, `DESIGN.md` y el contrato de superficie aplicable.
2. Leer la referencia del comando que corresponda y `reference/craft-floor.md` inmediatamente antes de editar.
3. Revisar la interfaz renderizada, no solo el código, en escritorio, laptop, tablet y móvil, incluidos teléfono pequeño y horizontal cuando aplique. Una medición no sustituye mirar las capturas.
4. Ejecutar el detector de Impeccable, tratar sus hallazgos y actualizar `DESIGN.md` si el sistema visual cambia.
5. Informar con evidencia qué se revisó y qué quedó sin verificar.

Si la skill no está disponible, detenerse e informar al responsable; no sustituirla por criterio propio ni declararla aplicada.

Reglas:
- Diseño SaaS empresarial, moderno y profesional.
- Interfaz responsive obligatoria para escritorio, laptop, tablet y móvil.
- Sidebar, topbar y navegación consistentes.
- Tablas, formularios, menús y paneles correctamente adaptables; tablas legibles y formularios rápidos.
- Jerarquía visual clara y uso coherente de colores.
- Tipografía, espaciado y componentes uniformes.
- Estados de carga, error, vacío y confirmación.
- Accesibilidad, navegación mediante teclado y controles táctiles.
- Evitar interfaces genéricas, inconsistentes o visualmente descuidadas.
- Evitar animaciones innecesarias y elementos decorativos excesivos.
- Priorizar la rapidez operativa del personal de despacho.

Mantener un sistema de diseño reutilizable mediante componentes y tokens visuales.

No copiar directamente interfaces protegidas de Yango o inDrive.

## 6. Integración con el backend
El backend ASP.NET Core 10 es la única autoridad de negocio.

- Inspeccionar los endpoints reales antes de implementarlos.
- Utilizar servicios HTTP tipados.
- No inventar rutas, propiedades ni respuestas.
- Gestionar correctamente errores 400, 401, 403, 404, 409 y 500.
- Incluir Idempotency-Key en operaciones críticas que lo requieran.
- Implementar paginación, filtros y validación de formularios.
- Sincronizar estados después de reconexiones.
- No confirmar visualmente una asignación antes de recibir respuesta del backend.
- Nunca acceder directamente a PostgreSQL o Neon desde React.
- No duplicar las reglas de negocio del motor de despacho.

## 7. Seguridad
- Utilizar la autenticación empresarial existente.
- Respetar cookies HttpOnly y Secure.
- Implementar el mecanismo CSRF definido por la API.
- Configurar proxy de desarrollo seguro hacia ASP.NET Core.
- No almacenar secretos ni tokens sensibles en localStorage.
- No utilizar credenciales de prueba como acceso empresarial.
- Respetar roles Administrador y Operador.
- Ocultar acciones no permitidas, sin considerar esto una autorización suficiente.
- El backend siempre debe validar los permisos.
- No exponer datos de empresas ajenas.

## 8. Convenciones de programación
- TypeScript con strict habilitado.
- Evitar any, duplicación y componentes gigantes.
- Usar componentes funcionales y hooks.
- Utilizar nombres descriptivos y consistentes.
- Reutilizar componentes cuando aporte claridad.
- Mantener separación entre presentación y lógica.
- Utilizar async/await y gestionar excepciones.
- Evitar dependencias circulares.
- No utilizar datos simulados como si fueran reales.
- No agregar funcionalidades no solicitadas.

## 9. Pruebas y calidad
Antes de completar una tarea:
- Ejecutar npm run build.
- Ejecutar el linter disponible.
- Ejecutar las pruebas relevantes.
- Revisar errores de TypeScript.
- Verificar diseño responsive.
- Comprobar accesibilidad básica.
- Validar autenticación, permisos y errores HTTP cuando corresponda.
- Verificar integración con endpoints reales si están disponibles.

No declarar funcionalidades probadas sin ejecutar las verificaciones correspondientes.

## 10. Flujo de trabajo y memoria obligatoria

### Antes de programar
1. Leer `../RADIOMOVIL/AGENTS.md`: arquitectura y reglas generales de RadioMovil.
2. Leer `../RADIOMOVIL/MEMORY.md`: estado general del proyecto.
3. Leer `./AGENTS.md`: reglas específicas del frontend.
4. Leer `./MEMORY.md`: progreso y decisiones del frontend.
5. Inspeccionar el código existente y los contratos reales del backend.
6. Planificar los cambios respetando las decisiones anteriores.

### Durante el desarrollo
1. Implementar por funcionalidades.
2. Cargar y aplicar Impeccable según la sección 5 en toda tarea de interfaz, también en cambios pequeños.
3. Respetar la arquitectura, autenticación y permisos existentes.
4. No modificar el backend ni sus reglas sin autorización.
5. Evitar repetir errores documentados en las memorias.

### Después de cada tarea
1. Ejecutar build, lint y pruebas correspondientes.
2. Actualizar `./MEMORY.md` con avances verificados, decisiones, errores y próximos pasos.
3. Actualizar `../RADIOMOVIL/MEMORY.md` únicamente cuando existan cambios importantes que afecten al proyecto general.
4. Mantener ambos archivos MEMORY.md con un máximo de 50 líneas cada uno.
5. Eliminar información obsoleta sin perder decisiones vigentes.
6. No inventar resultados ni declarar funcionalidades sin verificar.
7. Informar los cambios realizados y detenerse antes de la siguiente fase.

### Jerarquía de instrucciones
1. Instrucciones autorizadas del responsable y restricciones de seguridad.
2. `../RADIOMOVIL/AGENTS.md`: reglas generales del sistema.
3. `./AGENTS.md`: reglas específicas del frontend.
4. `../RADIOMOVIL/MEMORY.md`: contexto general verificado.
5. `./MEMORY.md`: progreso verificado del frontend.

**Regla permanente:** ambos AGENTS.md son normativos; los MEMORY.md documentan progreso y no pueden modificar las reglas arquitectónicas. El agente debe mantener la coherencia entre ambos componentes del proyecto.
## 11. Prohibiciones
- No crear otro backend.
- No modificar las invariantes del despacho.
- No implementar RabbitMQ.
- No acceder directamente a Neon.
- No exponer credenciales.
- No inventar conductores, viajes o estadísticas reales.
- No modificar Production ni Development.
- No realizar cambios destructivos sin autorización.
- No incorporar funcionalidades ajenas al proyecto.

## 12. Prioridad de implementación
1. Arquitectura frontend y conexión segura a API.
2. Inicio de sesión empresarial.
3. Layout administrativo.
4. Gestión de solicitudes.
5. Despacho manual asistido.
6. Gestión de conductores.
7. Mapa GPS y actualizaciones en tiempo real.
8. Administración, incidencias y reportes.
9. Pruebas integrales y optimización.

**Regla final:** desarrollar una herramienta empresarial confiable, rápida y fácil de utilizar. El diseño debe ayudar al operador a despachar carreras eficientemente, sin comprometer la seguridad ni las reglas del backend.
