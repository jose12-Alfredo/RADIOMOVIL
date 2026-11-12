# Contratos HTTP que consume la app (API ASP.NET Core 10, solo Neon Testing)

Fuente de verdad: código de `RADIOMOVIL/Controllers` y `/openapi/v1.json` de la API Testing. La app **no** define contratos: si falta uno, se solicita al responsable y lo implementa quien mantiene el backend.

## Autenticación (cliente Android, Bearer)

| Método | Ruta | Cuerpo | Respuesta |
|---|---|---|---|
| POST | `api/v1/auth/login` | `{email, password, empresaId, cliente:"Android"}` | 200 `{tokenType, accessToken, expiresIn, refreshToken}`; 401 credenciales/empresa/bloqueo |
| POST | `api/v1/auth/refresh` | `{refreshToken}` | 200 nuevos tokens; 401 si ya se usó, expiró o se revocó |
| POST | `api/v1/auth/logout` | — | 204, revoca la sesión en BD |
| GET | `api/v1/auth/me` | — | `{usuarioId, email, empresaId, roles[], expiraEn}` |

- Access token 10 min; refresh **de un solo uso**, 7 días. La app serializa las renovaciones (una a la vez) y renueva 30 s antes de expirar.
- Tokens en `flutter_secure_storage` (Android Keystore); `allowBackup=false`.
- Solo entra una membresía con rol `Conductor`; otro rol cierra la sesión.

## Conductor (`[Authorize]`, el conductor sale de la sesión)

| Método | Ruta | Notas |
|---|---|---|
| GET | `api/v1/conductor/yo` | `{conductorId, nombre, estado (Disponible/Ocupado/FueraDeServicio), ocupacion (Oferta/Viaje/null), turno {id, vehiculoId, patente, inicio} \| null, estadoGps (Reciente/Desactualizado/SinSenal), ultimaRecepcion, intervaloEnvioSegundos}` |
| GET | `api/v1/conductor/vehiculos` | `[{vehiculoId, patente, libre}]` autorizados por la central |
| POST | `api/v1/conductor/turno` | `{vehiculoId}` + `Idempotency-Key` → 201 `{id, repetida}`; 409 vehículo no autorizado/ocupado o turno ya abierto |
| POST | `api/v1/conductor/turno/cerrar` | `{motivo?}` (≤500) + `Idempotency-Key` → 200; 409 con oferta/viaje activo |
| POST | `api/v1/conductor/ubicaciones` | `{latitud, longitud, precisionMetros, capturadaEn ISO-8601}` → 200 `{aceptada, motivo (Repetida/MasAntiguaQueLaUltima/null), recibidaEn, estadoGps, intervaloMinimoSegundos}` |

Ubicaciones: 409 sin turno abierto (la app detiene el seguimiento), 429 + `Retry-After` si se envía antes de 3 s (la app envía cada `intervaloEnvioSegundos`, mínimo 5 s), 400 coordenadas/precisión (0–5000 m) inválidas o `capturadaEn` futura (>10 s) o vieja (>120 s). Estado GPS en servidor: Reciente ≤30 s, Desactualizado 31–90 s, SinSenal >90 s.

Idempotencia en la app: una clave UUID por intención; se reutiliza solo al reintentar tras error de red y se descarta tras éxito o respuesta definitiva (4xx).

## Asignación directa (fase E) — PUBLICADO en Testing 2026-11-10

**Decisión permanente (2026-11-10):** la central asigna la carrera; el conductor **no acepta ni rechaza**. La app no tiene esos botones ni usa los endpoints de ofertas (`.../ofertas/{intentoId}/aceptar|rechazar`). Comprobado desde el PC: las rutas responden 401 sin sesión (existen).

| Método | Ruta | Notas |
|---|---|---|
| GET | `api/v1/conductor/yo` | añade `carreraActivaId: uuid \| null`; con carrera Asignado/EnCurso, `ocupacion = "Viaje"` |
| GET | `api/v1/conductor/carreras` | Solo rol Conductor (403 si no). Arreglo (hoy 0 o 1): `{viajeId, solicitudId, estado ("Asignado"\|"EnCurso", texto), asignadoEn, recibidaEn?, origen, recogidaLatitud?, recogidaLongitud?, destino?, destinoLatitud?, destinoLongitud?, observaciones?, nombrePasajero?, telefonoPasajero?}` |
| POST | `api/v1/conductor/carreras/{viajeId}/recibida` | Sin cuerpo ni `Idempotency-Key` (idempotente por diseño) → 200 `{viajeId, recibidaEn, repetida}`; 404 ajena/inexistente; 409 cerrada. Es ENTREGA, no aceptación: llamarlo cuando la pantalla mostró la orden. La central ve "Vista hh:mm". |

Reglas de uso:
- Coordenadas WGS84 tal como las marcó la operadora; sin precisión. Si son null: solo texto, **no geocodificar**.
- Sin FCM (autorización pendiente): `GET /carreras` al abrir y al reconectar; en primer plano sondear `/yo` (~10 s); si `carreraActivaId` pasa de null a un id → `GET /carreras`, aviso "Nueva carrera asignada" con sonido/vibración y `POST recibida`.
- "Cómo llegar" solo con coordenadas: `google.navigation:q=LAT,LNG`, respaldo `https://www.google.com/maps/dir/?api=1&destination=LAT,LNG&travelmode=driving` o Waze `https://waze.com/ul?ll=LAT,LNG&navigate=yes`.
- Llamar al pasajero: `tel:` solo si llega `telefonoPasajero`.
- `turno/cerrar` con carrera activa → 409. Iniciar/finalizar viaje desde la app NO existe (requiere autorización aparte).

## Estados del viaje y ruta en la app — SOLICITADO 2026-11-10 (no publicado; no implementar)

Decisión del responsable (chat de la app, 2026-11-10): 4 pasos y servidor de rutas propio (OSRM/Valhalla autoalojado detrás de la API).

| Método | Ruta | Transición |
|---|---|---|
| POST | `.../carreras/{viajeId}/en-camino` | Asignado → EnCamino ("Ir a recoger") |
| POST | `.../carreras/{viajeId}/llegada` | EnCamino → EnPuerta ("Llegué") |
| POST | `.../carreras/{viajeId}/iniciar` | EnPuerta/EnCamino → EnCurso ("Iniciar recorrido") |
| POST | `.../carreras/{viajeId}/finalizar` | EnCurso → Finalizado ("Finalizar carrera"), `{observaciones?}` |
| GET | `.../carreras/{viajeId}/ruta?desdeLatitud&desdeLongitud` | `{haciaPunto, distanciaMetros, duracionSegundos, geometria (GeoJSON LineString), calculadaEn}`; 404 sin coordenadas; 503 sin servicio de rutas |

Transiciones con `Idempotency-Key` → 200 `{viajeId, estado, en, repetida}`; 404 ajena; 409 inválida/cerrada. Pendiente de que backend lo publique.

No existe contrato para cambiar la disponibilidad manualmente: la calcula el servidor.
