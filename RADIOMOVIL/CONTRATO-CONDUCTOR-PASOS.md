# Contrato publicado: pasos del conductor y ruta (Testing, 2026-11-10)

Autorizado por el responsable el 2026-11-10. Las rutas tienen la base `/api/v1/conductor`, con Bearer y una sesión de rol Conductor. La app nunca habla con Neon ni con el servidor de rutas.

## Pasos

Cada paso es un POST con `Idempotency-Key` obligatoria (máximo 160 caracteres; sin ella responde 400). El cuerpo es `{}`, salvo al finalizar.

| Ruta | Transición |
|---|---|
| `POST carreras/{viajeId}/en-camino` | Asignado → EnCamino («Ir a recoger») |
| `POST carreras/{viajeId}/llegada` | EnCamino → EnPuerta («Llegué», opcional) |
| `POST carreras/{viajeId}/iniciar` | EnCamino o EnPuerta → EnCurso («Iniciar recorrido»). No se puede iniciar desde Asignado. |
| `POST carreras/{viajeId}/finalizar` | EnCurso → Finalizado. Cuerpo opcional `{"observaciones": "…"}` (hasta 500 caracteres). |

**Respuestas**
- **200:** `{viajeId, estado, en, repetida}`. `estado` es EnCamino, EnPuerta, EnCurso o Finalizado; `en` es la hora del servidor.
  - Repetir con la misma clave devuelve el mismo resultado con `repetida: true`.
- **400:** falta la clave, o las observaciones superan los 500 caracteres.
- **403:** la sesión no es de un conductor.
- **404:** la carrera es de otro conductor o no existe.
- **409** (con un texto legible), en cualquiera de estos casos:
  - el paso está fuera de orden;
  - el paso ya se dio con otra clave;
  - la misma clave se usó con otro contenido;
  - el conductor no tiene turno abierto;
  - la carrera está cerrada o la central la canceló;
  - se enviaron observaciones en un paso que no es finalizar.

**Qué guarda y qué cambia**
- En cada paso el servidor guarda la hora y la última posición GPS aceptada del turno. Si no hay posición, no se inventa una.
- Después de Finalizado, la carrera desaparece de `GET carreras`, `/yo.carreraActivaId` queda en null y el conductor vuelve a «Disponible».
- `GET carreras` puede devolver los estados Asignado, EnCamino, EnPuerta o EnCurso.
- La central puede cancelar solo antes de recoger al pasajero: en Asignado, EnCamino o EnPuerta. Con el pasajero a bordo (EnCurso) no puede.
- Un turno no se puede cerrar con una carrera activa (409).

## Ruta

`GET carreras/{viajeId}/ruta?desdeLatitud=&desdeLongitud=`

**Respuestas**
- **200:** `{haciaPunto, distanciaMetros, duracionSegundos, geometria: {type: "LineString", coordinates: [[lng, lat], …]}, calculadaEn}`, con `Cache-Control: no-store`.
  - `haciaPunto` es «Recogida» en Asignado, EnCamino y EnPuerta, y «Destino» en EnCurso.
- **400:** faltan las coordenadas o están fuera de rango.
- **404:** la carrera es ajena o está cerrada, o falta el punto de recogida o de destino; el texto indica cuál.
- **503:** el servicio de rutas no está disponible.

**Servidor de rutas.** Lo calcula un OSRM propio, configurado en `Rutas:OsrmUrl` (variable de entorno `Rutas__OsrmUrl`). Mientras no se configure, la ruta responde siempre 503. **Hoy todavía no está alojado.** La app debe tratar el 503 como un caso normal y mostrar solo los puntos y el texto. Pide la ruta como máximo cada 15 s, o cuando el conductor se desvíe más de 60 m.
