# Base técnica y entorno Testing

## Puesta en marcha en otro equipo
Copiar `RADIOMOVIL` completo, con carpetas ocultas (`.claude`, `.agents`, `.opencode`) y `.neon`. Se pueden omitir `node_modules`, `bin`, `obj`, `dist` y `test-results`. El `.env.local` de la raíz contiene credenciales de Production: no subirlo a la nube ni enviarlo por correo.
1. Instalar .NET SDK 10, Node.js, Edge y la CLI de Neon; iniciar sesión en Neon.
2. `npm.cmd ci` en la raíz y en `frontend-web`; `dotnet restore RADIOMOVIL.slnx`.
3. `dotnet dev-certs https --trust` y aceptar el aviso de Windows.
4. Exportar el certificado fuera del repositorio con `dotnet dev-certs https --export-path <ruta>.pem --format PEM --no-password`, y actualizar en `frontend-web/.env.local` `FRONTEND_HTTPS_CERT`, `FRONTEND_HTTPS_KEY` y `TESTING_API_CA`. Las rutas actuales son del equipo anterior.
5. Levantar la API con la conexión de `br-morning-pine-bdzyesoo` solo en memoria (ver «Levantar API») y Vite con `npm.cmd run dev`.
6. Sin comprobar todavía: las claves Data Protection en Testing están cifradas con DPAPI del equipo anterior, y otro equipo no puede descifrarlas. Vigilar el primer arranque y `/auth/csrf`; volver a iniciar sesión. La contraseña del administrador no depende de esas claves.

## Fase 8.6 — base previa de frontend (histórico; ver actualización Fase A más abajo)
Estructura Consola de llamada elegida por el responsable; contrato en `.impeccable/surfaces`. Rutas: `/panel`, `/solicitudes`, `/solicitudes/:id`, `/despacho` y `/cuenta` (`/session` redirige). Sidebar completa desde 1280 px, en iconos de 1024 a 1279, menú desplegable por debajo. Tabla como tarjetas por ancho de contenedor (consulta `@container`).
Endpoints consumidos: `GET auth/me`, `GET auth/csrf`, `POST auth/login`, `POST auth/logout`, `GET solicitudes` (estado, page, pageSize), `GET solicitudes/{id}`, `POST solicitudes` e `GET despacho/candidatos`. El panel obtiene sus cifras del campo `total` por estado. Sin tiempo real: refresco de 15 a 20 s con pestaña visible y al volver a ella.
Idempotencia: clave nueva por envío (`crypto.randomUUID`). Ante fallo de red, 500 o respuesta no interpretable, clave y origen se conservan, el campo se bloquea y Reintentar reutiliza ambos. Descartar es decisión explícita del operador. `revalidate` comprueba la sesión al recuperar el foco sin desmontar la aplicación ni perder lo escrito.
La aplicación abre en `/solicitudes`. El registro vive en `ConsoleLayout`, por encima de las rutas. Menores abiertos: la fila fijada conserva el estado de su confirmación (corregir antes de activar ofertas) y el campo no recupera el foco al volver desde Cuenta. Verificación: 20 unitarias y 20 pruebas Edge (4 de login y 16 del espacio de trabajo) con transporte interceptado y datos sintéticos, sin red; capturas en 7 tamaños por pantalla en `.impeccable/review`. **Esto no prueba integración real.** Las rutas nuevas no se han ejecutado contra la API de Testing.

### Central — histórico anterior a Fase A
No existe entidad Central. Hay `solicitudes.CentralId` y `contadores_central` (empresa, central, último orden), y el backend acepta cualquier GUID sin validarlo. En Testing, la empresa de prueba tiene 0 contadores, 0 solicitudes, 0 conductores, 0 vehículos y 0 turnos. Por eso `VITE_CENTRAL_ID` sigue sin valor y la interfaz bloquea el registro. Procedimientos posibles, ninguno toca el motor de despacho:
- A, sin cambiar el backend: el responsable designa un GUID como central de la instalación; la primera solicitud crea su fila en `contadores_central`, y desde ahí es comprobable. Antes de esa solicitud no hay nada que verificar y cualquier GUID sería aceptado.
- B, verificable, requiere autorización: tabla `centrales` (Id, EmpresaId, Nombre, Activa), migración solo Testing, `GET /api/v1/centrales` limitado a la empresa de la sesión y rechazo en el registro de centrales ajenas o inexistentes. El frontend la usaría sin configuración manual.

### Fase A — implementación actual (2026-11-09)
Procedimiento B autorizado: `centrales` con empresa, nombre único por empresa y estado activo; administrador crea con `POST /api/v1/centrales` (nombre; ID generado por servidor), operador/administrador consulta activas con `GET /api/v1/centrales`. Todas las centrales activas de la empresa son accesibles por sus operadores; todavía no existe asignación individual operador-central. Registro valida en transacción central activa de empresa con FOR SHARE antes de consumir la clave; FK compuesta impide referencias ajenas. Migración `FaseACentralesYRegistro` aplicada solo a Testing: centrales históricas preservadas como inactivas, no seleccionables.
`POST /api/v1/solicitudes` conserva `centralId`, `origen`, `Idempotency-Key` y añade opcionales `telefonoPasajero` (40), `nombrePasajero` (160), `destino` (500), `observaciones` (1000), `latitud`/`longitud` (ambas o ninguna; límites geográficos). Posición PostGIS Point(4326) se persiste si se proporciona, sin geocodificación implícita; la API devuelve estos campos en POST/GET/lista. Replays comparan todos los datos. React consulta nombres/IDs al servidor y bloquea registro sin central activa; los opcionales están en panel expandible. Coordenadas se incorporarán mediante selección de mapa en Fase D, no mediante GUID ni lat/lon manual.
Verificación: dos pruebas nuevas sobre Neon Testing real (registro transaccional, aislamiento e idempotencia; API real con autenticación Testing, central creada, lectura/registro/replay/ajena). Pruebas históricas adaptadas para crear centrales reales; regresión conectada completa 80/80, 0 omitidas. Frontend build/lint, 21 unitarias y 20 pruebas Edge offline, capturas desktop/tablet/móvil revisadas. No equivalen a login del responsable en navegador: para usar el panel con su empresa primero crear su central operativa con nombre decidido por él mediante POST autenticado por administrador.

### Pendientes de backend registrados antes de Fase A
- Registro telefónico: teléfono, pasajero y destino no se persisten; el sistema definitivo los necesita.
- Oferta activa: contrato mínimo propuesto, solo lectura y limitado a la empresa de la sesión, `GET /api/v1/despacho/solicitudes/{id}/estado` devuelve `{ solicitudId, estado, ofertaActiva: { intentoId, conductorId, conductorNombre, venceEn } | null, viaje: { viajeId, conductorId, estado } | null }`. Sin él no se habilitan ofertas: tras recargar no se podría controlar la oferta.
- Conductores, vehículos y turnos: no hay endpoints; los datos de prueba quedan pendientes de autorización específica.
- Usuarios: no hay listado; por eso la web no incluye administración de usuarios. Administrador y Operador ven hoy la misma navegación porque el backend les permite lo mismo en estas rutas.
- Cancelar por cliente: existe en la API; no se expuso por estar fuera del alcance autorizado.

Límites comprobados antes de Fase A (los contratos de centrales y llamada se actualizaron arriba):
- `POST /solicitudes` exige `CentralId`, `Origen` (hasta 500) e `Idempotency-Key`. No guarda teléfono, cliente ni destino. No hay catálogo de centrales. El orden `OrdenRecepcion` solo se aplica al listar con `centralId`.
- `GET /solicitudes` pagina hasta 100 y filtra por `centralId` y `estado`. `estado` llega como número (sin `JsonStringEnumConverter`): 0 Pendiente, 1 Ofertada, 2 Asignada, 3 EnViaje, 4 Finalizada, 5 Cancelada. Se envía como nombre en el filtro. Los totales por estado se obtienen del campo `total`.
- `GET /despacho/candidatos` devuelve `conductorId`, `nombre` y `ubicacionVerificada` (hoy siempre falso). Sin distancia, tiempo ni GPS.
- `POST .../ofertas` devuelve `intentoId`, pero ningún GET expone oferta activa, viaje, conductor ni historial. Tras recargar no se puede actuar sobre la oferta.
- Aceptar sin restricción de rol en controlador, pero el guard exige conductor propio; rechazar y cancelar-conductor son de Conductor. Cancelar por cliente con motivo es de Operador/Administrador.
- No hay endpoints para crear conductores, vehículos ni turnos. Usuarios: crear y modificar, sin listar.
- Sin tiempo real: refresco de GET al recuperar foco y por intervalo con la pestaña visible.

## Ejecución local
1. `npm.cmd install` (o `npm.cmd ci` con lockfile).
2. Comprobar certificado: `dotnet dev-certs https --check --trust`. Si no es confiable, ejecutar `dotnet dev-certs https --trust` y aceptar la confirmación de Windows.
3. Exportar fuera del repositorio: `dotnet dev-certs https --export-path "$env:LOCALAPPDATA\Temp\opencode\radiomovil-local.pem" --format PEM --no-password`. La clave `.key` es privada; no compartir ni versionar.
4. Copiar `.env.example` a `.env.local` y configurar rutas a certificado y clave.
5. `npm.cmd run dev`: https://localhost:59257. Puerto fijo para evitar cambios de origen.

## Proxy y cookies
El proxy permanece desactivado mientras TESTING_API_URL esté vacío. Antes de configurarlo, comprobar que el proceso de API se inició con ASPNETCORE_ENVIRONMENT=Testing, NEON_TESTING_BRANCH=testing y conexión exclusivamente Testing. Los perfiles existentes de launchSettings son Development: no usarlos para esta integración.
API_ENVIRONMENT=Testing es una declaración local, no prueba el entorno del servidor: actualmente la API no publica un contrato para atestiguarlo. Confirmarlo con el responsable del host.
El destino debe ser HTTPS local. Configurar TESTING_API_CA con el certificado público de confianza de la API cuando Node no lo reconozca; nunca desactivar secure ni NODE_TLS_REJECT_UNAUTHORIZED.
Frontend y navegador utilizan siempre `/api/v1/...` por el mismo origen HTTPS. El proxy conserva Set-Cookie y Cookie, sin reescribir HttpOnly, Secure ni SameSite=Strict. No se necesitan tokens bearer ni CORS en React.

## Cliente HTTP
`src/services/http.ts`: respuestas unknown validadas por decoder, GET sin CSRF y mutaciones con token fresco; credenciales same-origin, sin caché ni redirecciones. No guarda sesiones/tokens en almacenamiento persistente ni reintenta mutaciones.
HttpError conserva status y cuerpo (JSON, texto o vacío). El consumidor distinguirá 400/401/403/404/409/500; un 400 no implica necesariamente fallo CSRF. NetworkError no implica que una escritura haya sido revertida. AbortSignal permite cancelar consultas.
Idempotency-Key se pasa explícitamente; conservar la misma clave y contenido al reintentar una operación incierta. No generar otra clave automáticamente.

## Validación pendiente de integración
Con host Testing y usuario autorizado: GET csrf debe emitir rm.csrf HttpOnly/Secure/Strict; login Web con X-CSRF-TOKEN debe emitir rm.web con esos atributos; volver a obtener CSRF tras login. Comprobar cookie almacenada/enviada por navegador y logout protegido. Validar 401, 403 y CSRF inválido.
Estas verificaciones requieren API real. Una respuesta de la plantilla o una prueba con fetch simulado NO prueba cookies del backend.

## Contrato implementado — fase 8.3, pendiente de integración real
### GET /api/v1/auth/me
Devuelve `{ usuarioId, email, empresaId, roles, expiraEn }`. IDs GUID, email string o null, roles lista con el único rol empresarial vigente, expiraEn fecha ISO de expiración de la sesión persistida. No es la expiración del bearer Android. No devuelve tokens, claves ni identificador de sesión.
Identidad/empresa se derivan exclusivamente de la autenticación. Lee sesión, usuario activo y membresía en una consulta; no permite seleccionar otra empresa mediante parámetros. Cache-Control no-store en respuestas del controlador.
401: sesión ausente, expirada, revocada, versión inválida o usuario inactivo. 403: sesión válida sin membresía activa/rol reconocido. Los cambios de membresía mediante el endpoint administrativo existente revocan sesiones y por ello normalmente producirán 401.
`src/features/auth/session.ts` incorpora decoder estricto, getSession, loginWeb y logoutWeb; sin pantalla ni almacenamiento persistente. loginWeb valida la respuesta y consulta me; el cliente HTTP obtiene CSRF fresco antes de cada mutación.

### Empresa antes del login — configuración por instalación
El responsable configura VITE_EMPRESA_ID en `.env.local` con el identificador de una empresa REAL autorizada en Testing, y reinicia Vite (recompilar para cambiar un bundle). Se incluye en el cliente como dato público, no como secreto ni autorización. No colocar contraseñas/conexiones en variables VITE_.
loginWeb solo recibe correo/contraseña; toma la empresa de esa configuración y falla antes de enviar credenciales si falta o es inválida. No hay empresa por defecto, selector ni resolución por correo. El backend sigue comprobando la membresía.
No se provisionó valor: falta una empresa real verificada. Las identidades sintéticas de los tests son fixtures aislados, no accesos operativos.

### Centrales permitidas — bloqueado por modelo de negocio
No existe entidad/catálogo Central ni asignación operador-central: solo CentralId en solicitudes y ContadorCentral (empresa/central/último orden). Tampoco existe catálogo de empresas con nombres comerciales en el DbContext actual.
Propuesta pendiente: definir catálogo de centrales por empresa y decidir si la autorización es empresarial o explícita por operador. Solo después definir consulta autenticada y validar ese alcance en el registro. Esto excede el contrato de sesión autorizado y no se implementó.
No convertir contadores o solicitudes históricas en catálogo de centrales autorizadas.

## Resultados fase 8.3
- Backend compilado y suite ejecutada: 15 aprobadas, 40 omitidas, 0 fallos (55 total). Las dos nuevas pruebas de me con sesiones son integración Testing y quedaron omitidas por falta de variables requeridas.
- SessionHttpTests verifica el endpoint real en TestServer: anónimo recibe 401, incluso con usuario/empresa en query, sin redirección ni cuerpo de identidad. Interceptor prohíbe abrir conexiones; no es una prueba TLS de navegador.
- Frontend build/lint correctos; 8/8 pruebas unitarias con fetch simulado. No prueban cookies reales de navegador.
- Windows continúa sin confiar en el certificado. No hay configuración Testing en el proceso ni URL/empresa verificadas; proxy sin destino, sin conexión a otros entornos.
- Pendiente: aprovisionar configuración segura del host Testing y los requisitos de NeonFact, confiar certificado y validar flujo real desde navegador. No pegar credenciales en documentación ni usar cuentas sintéticas como empresariales.
- El usuario autorizó continuar sin conexión. Se comprobó ausencia de variables Testing en proceso/usuario/sistema y de API en puertos conocidos; las conexiones del .env.local general no se cargaron ni utilizaron.

## Fase 8.4 — interfaz de autenticación
- `/login`: correo/contraseña, mostrar/ocultar, validación, errores y aviso de configuración pendiente. Sin empresa configurada no envía credenciales.
- `/session`: ruta protegida por identidad real, empresa instalada y roles Administrador/Operador. Solo cuenta/rol y logout; ningún dashboard.
- AuthProvider restaura mediante getSession, revalida al recuperar foco y al vencimiento declarado, cancela operaciones al desmontar y evita duplicación. Cookies/CSRF reutilizan los servicios existentes.
- Una consulta de sesión fallida tras login aceptado se distingue con SessionLookupError: se permite reconsultar sin reenviar credenciales. Un error de logout no se anuncia como cierre confirmado.
- Estado inicial 401 significa anónimo, no expiración demostrada. Si existía sesión conocida y deja de ser válida, se informa al usuario. Roles o empresa incompatibles bloquean el espacio.
- No hay localStorage ni sesión de demostración. Contraseña se vacía al enviar; no se registra ni persiste.
- `npm.cmd run build`, `npm.cmd run lint` y `npm.cmd test`: correctos, 11 unitarias. `npm.cmd run test:browser`: 4 pruebas Edge offline, con Playwright como dependencia exclusiva de desarrollo.
- Pruebas browser generan un build separado SIN empresa en test-results/offline-build y sirven sus archivos mediante interceptación local de requests; no conectan APIs, no aceptan certificados inválidos, no fabrican autenticación.
- Capturas `.impeccable/review/{desktop,tablet,mobile}.png` revisadas a 1440/820/390px. Teclado, toggle, bloqueo de /session y ausencia de scroll horizontal también comprobados a 320px. No equivalen a auditoría completa WCAG.
- Login exitoso, permisos y logout contra Testing real siguen pendientes de configuración. La revisión visual solo cubre configuración pendiente; mensajes de error tienen pruebas unitarias, no sesiones exitosas simuladas.

## Fase 8.5 — comprobaciones realizadas y bloqueo real
Se inspeccionaron variables de proceso/usuario/sistema, archivos .env locales, claves de appsettings, csproj, Program.cs y perfiles de arranque sin imprimir secretos. El .env.local de la raíz declara NEON_BRANCH=production: NO se cargaron ni utilizaron sus conexiones. No hay variables Testing ni UserSecretsId en la API; Program.cs obtiene la conexión desde variables de entorno, no desde los .env. Ningún perfil launchSettings está configurado como Testing.
No se encontró API en los puertos locales conocidos 7284/5176. El puerto frontend 59257 está ocupado y no se interrumpió su proceso.

| Requisito faltante | Dónde configurarlo / cómo verificarlo |
| --- | --- |
| Conexión real autorizada Testing | Variables del proceso que arrancará API: NEON_TESTING_DIRECT_CONNECTION_STRING (o NEON_TESTING_CONNECTION_STRING). Obtenerla por canal local seguro; verificar previamente en Neon que el endpoint corresponde al proyecto/rama testing, no confiar solo en el nombre de la variable. No copiar DATABASE_URL del .env general. |
| Selección de entorno | En ese proceso: ASPNETCORE_ENVIRONMENT=Testing y NEON_TESTING_BRANCH=testing. Arranque con `dotnet run --project RADIOMOVIL/RADIOMOVIL.csproj --no-launch-profile` desde la raíz, únicamente tras verificar conexión y asignar un listener HTTPS local. No arrancar con los perfiles Development. |
| Certificado confiable | Ejecutar localmente `dotnet dev-certs https --trust` y aceptar Windows; comprobar con `--check --trust`. Configurar certificado/key de Vite fuera del repositorio. No omitir validación TLS. |
| API local comprobada | Cuando el host Testing arranque, verificar su listener HTTPS/certificado; colocar su URL real en frontend-web/.env.local: TESTING_API_URL, API_ENVIRONMENT=Testing y TESTING_API_CA si Node necesita la CA explícita. Actualmente URL y CA están vacías. |
| Empresa y cuenta reales | Responsable proporciona empresa existente con membresía activa y cuenta autorizada; VITE_EMPRESA_ID en frontend-web/.env.local y reinicio Vite. Credenciales solo en navegador o canal local seguro, nunca documentación o chat. No se creó ninguna identidad. |
| Pruebas NeonFact existentes | Proceso de pruebas requiere NEON_TESTING_DIRECT_CONNECTION_STRING y NEON_TESTING_BRANCH. Sustituido: ya no se exigen comparadores Development/Production; ver «Protección Testing» más abajo. |

Resultados 8.5: backend 15/55 aprobadas, 40 omitidas por guardias; frontend build/lint y 11 unitarias correctos; 4 Edge offline pasan incluyendo captura de errores console/pageerror (ninguno en las vistas verificadas). HTTPS Vite /login responde 200 en 59258 con CA explícita validada por Node; servidor temporal cerrado. Esto no establece confianza Windows/navegador ni valida una API.
El flujo CSRF → login → me → sesión → logout y credenciales incorrectas/revocación/permisos contra DB real no se ejecutaron. Proxy intacto y desactivado; sin operaciones DB en ningún entorno. Fase 8.5 bloqueada para integración real, no cerrada. No se considera cumplido el requisito de autenticación real antes de iniciar dashboard.

### Reverificación 8.5 (2026-11-06)
Sin conexión a ninguna base de datos ni obtención de cadenas de conexión. Solo metadatos de la CLI de Neon.
- Ramas: `testing`, `development` y `production` en estado ready, con un endpoint read_write cada una y 3 hosts distintos. Solo `production` es la rama predeterminada. Ninguna está protegida.
- Riesgos de selección: `.neon` fija `development` y el `.env.local` raíz declara production. `neon connection-string` sin argumento usa la rama predeterminada: indicar siempre `testing`.
- Listener verificado: la API compilada, en Testing con `https://localhost:7284` y host de DB `offline.invalid`, arranca. `/health` 200; `/api/v1/auth/me` 401 sin cuerpo, también con empresaId en query; TLS validado por Node con la CA explícita. `/api/v1/auth/csrf` 500 sin DB porque las claves Data Protection residen en ella: cookies y CSRF siguen sin probar.
- Certificado: `FRONTEND_HTTPS_CERT` es el mismo certificado de desarrollo de .NET (vence 2027-04-28), utilizable también como `TESTING_API_CA`. Windows sigue sin confiar en él (`--check --trust` exit 7).
- Vite en 59257 pertenece a este frontend, se inició antes de esta verificación y no se interrumpió: debe reiniciarse al cambiar `.env.local` (`strictPort`).
- `VITE_EMPRESA_ID` no existe en `.env.local`. No hay catálogo de empresas: una empresa es un GUID con membresías, y sin acceso a DB no se puede confirmar cuáles existen.
- Comparadores NeonFact: `TestingConnection()` y el benchmark solo los analizan, sin abrir conexión. Por lectura de `NeonConnection.Validate`, el formato Npgsql sin contraseña (Host, Database, Username, Ssl Mode=VerifyFull) es aceptado; no ejecutado todavía. El formato URI exige contraseña.
- Brecha de guardia: `Program.cs`, `EnterpriseAuthNeonTests` y `DespachoNeonTests` no comparan hosts; `NEON_TESTING_BRANCH=testing` es una declaración. Antes de ejecutar, comparar el host de la conexión con el endpoint de la rama testing en la API de Neon. `NEON_TESTING_CONNECTION_STRING` tiene prioridad en Program.cs: no dejarla apuntando a otro destino.
- Las pruebas conectadas crean identidades sintéticas `example.invalid` en Testing y no las limpian.
- Bootstrap: `RADIOMOVIL_BOOTSTRAP_*` solo para un arranque; repetirlo falla porque la empresa ya tiene administrador.
- Resultados: backend build 0/0 y 15 aprobadas/40 omitidas; frontend build y lint correctos, 11/11 unitarias. Edge offline: 1 fallo aislado en `login.spec.ts:52` (foco de «Saltar al contenido») en la primera corrida, luego 3/3 de esa prueba y 4/4 de la suite en 4 corridas. Causa no confirmada: tratar como posible inestabilidad.

### Protección Testing y verificación real (2026-11-06)
Rama `testing` = `br-morning-pine-bdzyesoo`, proyecto RADIO MOVIL, endpoint `ep-shy-math-bdinnn5q`. Confirmado por la API de Neon y por el servidor (`neon.project_id`, `neon.branch_id`, `neon.endpoint_id`).
- `RadioMovil.Infrastructure/NeonTestingGuard.cs`: lista de permitidos con el host directo y el pooler de ese endpoint; admite también destinos inalcanzables (`.invalid`, 127.0.0.1:1). Aplicada en Program.cs antes de registrar persistencia, en la migración Testing y en las pruebas conectadas. Si el endpoint se recrea, verificar de nuevo y cambiar la constante. Las pruebas `NeonFact` se omiten si la conexión no es aceptada: comprobar `Omitido: 0` al ejecutarlas.
- Obtener la conexión siempre por ID y solo en memoria: `neon connection-string br-morning-pine-bdzyesoo --ssl verify-full`.
- Solo lectura: migraciones iguales al código (6). 70 usuarios, 70 sintéticos; 0 empresas con administrador real, antes de las ejecuciones de este día.
- Testing: 8/8 `EnterpriseAuthNeonTests` y suite completa 78/78, 0 omitidas (TestServer; crean datos sintéticos que no se limpian). API real en `https://localhost:7284` con TLS validado: `/health` 200, `/auth/me` 401, `/auth/csrf` 200 con `rm.csrf` Secure/HttpOnly/SameSite=Strict.
- Datos sintéticos tras las ejecuciones: 129 usuarios en `example.invalid` (116 `test`, 7 `registered`, 6 `bootstrap`), 59 más que los 70 previos; 87 empresas con membresía (+43), 137 sesiones (+62), 6 auditorías BootstrapAdmin (+3). Corresponden a 2 corridas de auth y 1 de suite completa. No se eliminó nada. Las pruebas también dejan otros registros sintéticos no contados aquí (solicitudes, ofertas, eventos).
- Alcance: la sesión empezó a las 18:58Z. En Neon, `testing` arrancó compute a las 19:17:32Z; `development` está suspendida desde las 14:56Z y `production` sin actividad desde el 2026-11-05. Las cadenas se pidieron solo por el ID de `testing`; de las otras ramas solo se leyeron metadatos. `.env.local` raíz, `.neon` y `frontend-web/.env.local` sin cambios en la sesión.
- NU1903 (GHSA-p3jp-9v83-76w9, Brotli de .NET): la advertencia vino del proyecto temporal `verify-testing.cs` por `Microsoft.DotNet.ILCompiler` 10.0.9, que los archivos .cs ejecutados con `dotnet run` traen por defecto. Ningún proyecto de la solución lo referencia y `dotnet list package --vulnerable --include-transitive` no informa paquetes vulnerables. Ese comando no revisa el runtime compartido: el instalado es 10.0.9 y la corrección es 10.0.14. Según el aviso, ASP.NET Core y HttpClient no descomprimen Brotli por defecto, y el código no usa Brotli, ResponseCompression ni RequestDecompression. Actualizar SDK/runtime antes de Production.
- Integración por proxy (2026-11-06): certificado confiable en Windows. `.env.local` frontend con `TESTING_API_URL=https://localhost:7284`, `TESTING_API_CA` y `VITE_EMPRESA_ID=05820d20-0d1d-4174-af46-a9cb859c5f73`, empresa de prueba creada por decisión del responsable. Administrador creado por bootstrap; contraseña introducida solo por el responsable. En Edge sin excepciones de certificado: `/auth/me` 401 sin sesión y `/auth/csrf` 200 con `rm.csrf` Secure/HttpOnly/Strict.
- Login real: el responsable entró desde su navegador y vio `/session` con su cuenta y rol. En DB: 1 sesión activa y auditoría Login. Atributos de `rm.web` verificados en pruebas TestServer, no leídos del navegador del responsable. Logout real confirmado: el responsable cerró sesión desde su navegador y la DB registra la sesión revocada y la auditoría Logout. Revocación en navegador descartada por el responsable: cubierta por pruebas contra Testing. Fase 8.5 cerrada.
- Levantar API: variables de Testing en el proceso (conexión por ID de rama, solo en memoria) y `dotnet RADIOMOVIL.dll` con `ASPNETCORE_URLS=https://localhost:7284`, sin variables de bootstrap.
- Bootstrap: requiere `RADIOMOVIL_BOOTSTRAP_ADMIN=1` y `_EMPRESA_ID`, `_EMAIL`, `_PASSWORD`, solo en un arranque. Contraseña de 12 o más caracteres con mayúscula, minúscula, dígito y símbolo. Falla si la empresa ya tiene administrador; repetirlo con éxito no es posible.

## Selector de ubicaciones (2026-11-10)
- `src/features/requests/location.ts` interpreta SIN red: «lat, lng» (coma, espacio o punto y coma), grados/minutos/segundos (N/S/E/W/O), `geo:` y enlaces de hosts `google.<tld>` / `maps.google.*` con coordenadas explícitas (`!3d!4d` del lugar > `q`/`query`/`ll`/`destination`/`daddr`/`center` > `/place|search/lat,lng` > `@lat,lng` de la vista). Valida rangos y rechaza 0,0. Nunca geocodifica ni sigue redirecciones; otros hosts (incluidos IP/localhost) se rechazan.
- **Bloqueo (requiere autorización):** los enlaces abreviados `maps.app.goo.gl` / `goo.gl/maps` no contienen coordenadas; resolverlos exige que la API siga la redirección hacia Google (servicio externo, lista blanca estricta de hosts, sin seguir otros destinos). Hoy se muestra cómo obtener el enlace completo o las coordenadas.
- Vista inicial del mapa: puntos marcados → preferencia LOCAL `localStorage['radiomovil.mapa.vistaInicial.v1']` ({lat,lng,zoom}, guardada solo al pulsar «Usar esta vista al abrir el mapa»; no es dato sensible ni posición de nadie) → GPS real reciente de la flota. La central aún no tiene «área operativa» en la API: añadirla es un cambio de contrato pendiente.
- El diálogo vive FUERA del `<form>` de registro (su búsqueda tiene envío propio) y trabaja sobre un borrador; solo «Confirmar ubicación» cambia los puntos que se envían con la misma clave idempotente.

