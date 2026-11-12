# MEMORY.md — RadioMovil Frontend Web
> Máximo 50 líneas. Normas: ../RADIOMOVIL/AGENTS.md; contexto: ../RADIOMOVIL/MEMORY.md.

## Base vigente
- Proyecto original React/TS strict/Vite; Tailwind/plugin 4.3.5 y React Router 7.18.5.
- Una app desktop-first responsive, teclado/tacto y funciones esenciales en móvil.
- Decisión permanente (2026-11-06): Impeccable obligatorio en todo el frontend y para cualquier agente; norma en AGENTS.md, sección 5.
- HTTP unknown+decoder, cookies same-origin, CSRF fresco por mutación, AbortSignal y errores tipados.
- Sin tokens persistidos ni reintentos automáticos; conservar clave idempotente/contenido ante resultado incierto.
- HTTPS local 59257 y proxy con TLS validado solo a Testing (TESTING_API_URL; certificados en C:/Users/jose1/AppData/Local/Temp/opencode/). La variable de proceso TESTING_API_URL tiene prioridad: `vite --port 59258` con otra API sirve para verificar sin tocar la 59257.

## Fase 8.3 — contrato
- GET auth/me: usuarioId/email/empresaId/roles/expiraEn, no-store; 401 sesión inválida/revocada/expirada; 403 membresía/rol inválido.
- Empresa prelogin: VITE_EMPRESA_ID público por instalación, sin valor predeterminado ni GUID manual.

## Fase 8.4 — interfaz implementada
- /login con validación y configuración pendiente; AuthProvider restaura sesión (comprobando/anónimo/autenticado/denegado/incierto), revalida al recuperar foco y bloquea envíos duplicados.
- Rutas protegidas por sesión real, empresa y rol Administrador/Operador; `/session` redirige a `/cuenta`.
- Sin empresa válida no envía credenciales; login aceptado con fallo de me reconsulta sin reenviar credenciales.
- Logout fallido no anuncia éxito; confirmado usa mensaje informativo. Sin almacenamiento persistente ni sesión de demostración.
- Impeccable: dirección sobria aprobada; marca SVG, panel marino/blanco, responsive, foco y textos accesibles.
- PRODUCT.md/DESIGN.md/.impeccable/design.json y contrato de superficie actualizados.

## Verificaciones 8.4 y repetición 8.5
- npm.cmd run test:browser ejecuta login, espacio de trabajo y flota offline con datos sintéticos (Playwright solo devDependency).
- Detector Impeccable []; revisión independiente: layout aprobable, dos correcciones resueltas, SHIP en alcance offline.
- 8.5 (2026-11-06): rama testing verificada por CLI y por el propio servidor; NeonTestingGuard solo admite su endpoint.
- Testing: 129 usuarios sintéticos sin borrar (59 de 8.5) y 1 administrador de la empresa de prueba creado por bootstrap.
- Backend real: 78 aprobadas/0 fallidas/0 omitidas en Testing; auth 8/8 en TestServer. API real HTTPS 7284: me 401; csrf 200 con cookie Secure/HttpOnly/Strict.
- Proxy a https://localhost:7284; empresa de prueba Testing 05820d20-0d1d-4174-af46-a9cb859c5f73; development/production sin compute de esta sesión.
- Fase 8.5 cerrada: login y logout reales en navegador del responsable; DB con sesión revocada y auditorías Login y Logout.
- Responsive auth corregido con Impeccable: sin min-height fijo; 15 tamaños sin overflow y acción visible; sin dispositivos físicos.
- Frontend build/lint OK; 11/11 unitarias; Edge 4/4 (hubo 1 fallo aislado login.spec:52, causa sin confirmar).

## Fase 8.6 — implementada; integración real pendiente
- Consola de llamada con Impeccable: la app abre en solicitudes; panel, detalle, despacho en lectura y cuenta. Plantilla eliminada.
- 20 unitarias y 20 Edge con datos sintéticos sin red; revisor Impeccable: ship sobre hallazgos. Rutas nuevas AÚN NO ejecutadas contra API Testing.
- Fase A (2026-11-09): backend Testing con centrales por empresa y registro opcional de datos telefónicos + punto PostGIS; frontend consulta GET centrales activas, selecciona por nombre (automática si única), bloquea si no hay, registra y presenta detalles y cola reales. No introduce coordenadas manuales: espera selección futura de mapa. 21 unitarias, 20 Edge offline, build/lint y capturas responsive OK; falta login/alta de central de la empresa del responsable y prueba real del navegador autenticado.
- Fase A cerrada (2026-11-09): «Central Principal» real en Testing; el responsable registró la solicitud orden 1 en el navegador y se vio en cola y panel.

## Fase B — Flota (2026-11-09, Impeccable)
- Entrada «Flota» (/flota) con pestañas Conductores, Vehículos e Historial de turnos (decisión del responsable); el riel de registro sigue visible.
- Conductores: banda de 3 estados del servidor que filtra (?estado=), nota GPS honesta, tabla con placa en chip; abrir/cerrar turno inline en la fila (sin modal); ocupado sin acción. Altas solo visibles a Administrador; Operador abre/cierra turnos.
- `useIdempotentAction`: clave conservada y campos bloqueados ante resultado dudoso; Reintentar no duplica; 409/404/400 muestran el texto del servidor.
- Verificado: build/lint OK, 26 unitarias, 25 Edge (5 nuevas de Flota con datos sintéticos, sin desbordes de 320 a 1920 px); contratos reales GET 200 y 400 sin clave en API Testing nueva (7285) con la sesión del responsable. Detector: solo avisos de tamaños/colores ya usados por el espacio de trabajo y no documentados en DESIGN.md.

## Fase C (2026-11-09/10) y próximos pasos
- Acceso del conductor y vehículos autorizados en su fila (solo Administrador), emisor GPS temporal /conductor y pestaña Ubicaciones (MapLibre 6.15, ruta diferida). Prueba real: Jorge envía GPS desde la app Flutter y aparece «GPS reciente». Fix 2026-11-10: maplibre-gl.css (sin capa) impone position:relative al contenedor y anulaba `inset:0` de @layer → mapa en una franja; ahora width/height 100%, ResizeObserver→resize() y encuadre tras resize. 31 unitarias, 28 Edge (con comprobación lienzo = marco).
- Asignación directa (2026-11-10, Impeccable): riel con «Ubicación en el mapa» + «Abrir mapa grande» (selector amplio: diálogo 85vw/pantalla completa en móvil, pegar enlace de Google Maps o coordenadas, arrastrar marcadores, borrador hasta «Confirmar ubicación»; enlaces abreviados bloqueados hasta autorizar servicio externo; vista inicial = preferencia local explícita o GPS real de la flota). Detalle pendiente: «Asignar carrera» con mapa y lista radio ordenada por el servidor; distancia «en línea recta», nunca ETA; Sin señal sin posición. Confirmación solo tras 201/200; dudoso conserva clave. Despacho: «Por asignar» y «Carreras activas» con «Vista hh:mm» / «Sin confirmar en la app». `shared/map/maplibre.ts` común (chunk diferido). Pasos del conductor visibles (solo lectura): Asignada / Yendo a recoger / En la puerta / Pasajero a bordo + «Desde las hh:mm» (`ultimoPasoEn`). 44 unitarias, 35 Edge (selector probado de 320 a 1920 px y giro). Próximo: retirar la API 7284 (59257 da 404 en Flota).
