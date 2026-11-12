import { request } from '../../services/http.ts'
import { isGuid } from '../requests/requests.ts'
import { gpsStates } from '../fleet/fleet.ts'
import type { GpsState } from '../fleet/fleet.ts'
import type { OperationResult } from '../fleet/fleet.ts'

// Asignación DIRECTA (decisión 2026-11-10): la central asigna y el conductor recibe la orden sin aceptarla.
// La distancia la calcula el backend (PostGIS) con GPS real del turno; React solo la muestra.

export type NearbyCandidate = { conductorId: string; nombre: string; patente: string; estadoGps: GpsState; antiguedadSegundos: number | null;
  latitud: number | null; longitud: number | null; distanciaMetros: number | null }
export type NearbyCandidates = { solicitudId: string; estadoSolicitud: string; recogidaLatitud: number | null; recogidaLongitud: number | null; items: NearbyCandidate[] }
// Pasos que marca el CONDUCTOR desde su app (2026-11-10); la central solo los ve.
export const tripStates = ['Asignado', 'EnCamino', 'EnPuerta', 'EnCurso'] as const
export type TripState = typeof tripStates[number]
export const tripStateLabel: Record<TripState, string> = { Asignado: 'Asignada', EnCamino: 'Yendo a recoger', EnPuerta: 'En la puerta', EnCurso: 'Pasajero a bordo' }
export const tripStateClass: Record<TripState, string> = { Asignado: 'state-asignada', EnCamino: 'state-asignada', EnPuerta: 'state-puerta', EnCurso: 'state-enviaje' }
export type ActiveTrip = { viajeId: string; solicitudId: string; ordenRecepcion: number; origen: string; destino: string | null; estado: TripState;
  asignadoEn: string; recibidaEn: string | null; conductorId: string; conductorNombre: string; patente: string | null; ultimoPasoEn: string | null }

function record(body: unknown, message: string): Record<string, unknown> {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) throw new Error(message)
  return body as Record<string, unknown>
}
const isDate = (value: unknown): value is string => typeof value === 'string' && Number.isFinite(Date.parse(value))
const finiteOrNull = (value: unknown): value is number | null | undefined => value === undefined || value === null || (typeof value === 'number' && Number.isFinite(value))
const textOrNull = (value: unknown): value is string | null | undefined => value === undefined || value === null || typeof value === 'string'

export function decodeNearby(body: unknown): NearbyCandidates {
  const message = 'Contrato de candidatos inválido.'
  const value = record(body, message)
  if (!isGuid(value.solicitudId) || typeof value.estadoSolicitud !== 'string' || !finiteOrNull(value.recogidaLatitud) ||
    !finiteOrNull(value.recogidaLongitud) || !Array.isArray(value.items)) throw new Error(message)
  return {
    solicitudId: value.solicitudId, estadoSolicitud: value.estadoSolicitud,
    recogidaLatitud: value.recogidaLatitud ?? null, recogidaLongitud: value.recogidaLongitud ?? null,
    items: value.items.map(item => {
      const row = record(item, message)
      const estadoGps = gpsStates.find(state => state === row.estadoGps)
      if (!isGuid(row.conductorId) || typeof row.nombre !== 'string' || typeof row.patente !== 'string' || estadoGps === undefined ||
        !finiteOrNull(row.antiguedadSegundos) || !finiteOrNull(row.latitud) || !finiteOrNull(row.longitud) || !finiteOrNull(row.distanciaMetros) ||
        // «Sin señal» nunca trae posición ni distancia: el GPS viejo no se muestra como actual.
        (estadoGps === 'SinSenal' && (row.latitud != null || row.distanciaMetros != null))) throw new Error(message)
      return { conductorId: row.conductorId, nombre: row.nombre, patente: row.patente, estadoGps, antiguedadSegundos: row.antiguedadSegundos ?? null,
        latitud: row.latitud ?? null, longitud: row.longitud ?? null, distanciaMetros: row.distanciaMetros ?? null }
    }),
  }
}

export function decodeActiveTrips(body: unknown): ActiveTrip[] {
  const message = 'Contrato de carreras inválido.'
  if (!Array.isArray(body)) throw new Error(message)
  return body.map(item => {
    const row = record(item, message)
    const estado = tripStates.find(state => state === row.estado)
    if (!isGuid(row.viajeId) || !isGuid(row.solicitudId) || !Number.isInteger(row.ordenRecepcion) || typeof row.origen !== 'string' ||
      !textOrNull(row.destino) || estado === undefined || !isDate(row.asignadoEn) || !(row.recibidaEn == null || isDate(row.recibidaEn)) ||
      !isGuid(row.conductorId) || typeof row.conductorNombre !== 'string' || !textOrNull(row.patente) ||
      !(row.ultimoPasoEn == null || isDate(row.ultimoPasoEn))) throw new Error(message)
    return { viajeId: row.viajeId, solicitudId: row.solicitudId, ordenRecepcion: row.ordenRecepcion as number, origen: row.origen,
      destino: row.destino ?? null, estado, asignadoEn: row.asignadoEn, recibidaEn: (row.recibidaEn as string | null | undefined) ?? null,
      conductorId: row.conductorId, conductorNombre: row.conductorNombre, patente: row.patente ?? null,
      ultimoPasoEn: (row.ultimoPasoEn as string | null | undefined) ?? null }
  })
}

/** Resultado del contrato de despacho existente; para la pantalla, el identificador relevante es el viaje creado. */
export function decodeAssignment(body: unknown): OperationResult {
  const value = record(body, 'Respuesta de asignación inválida.')
  if (!isGuid(value.viajeId) || typeof value.repetida !== 'boolean') throw new Error('Respuesta de asignación inválida.')
  return { id: value.viajeId, repetida: value.repetida }
}

export const listNearby = (solicitudId: string, signal?: AbortSignal) =>
  request(`/api/v1/despacho/solicitudes/${encodeURIComponent(solicitudId)}/candidatos`, decodeNearby, { signal })
export const listActiveTrips = (signal?: AbortSignal) => request('/api/v1/despacho/carreras', decodeActiveTrips, { signal })
export const assignTrip = (input: { solicitudId: string; conductorId: string }, key: string) =>
  request(`/api/v1/despacho/solicitudes/${encodeURIComponent(input.solicitudId)}/asignar`, decodeAssignment,
    { method: 'POST', body: { conductorId: input.conductorId }, idempotencyKey: key })

const kilometres = new Intl.NumberFormat('es', { minimumFractionDigits: 1, maximumFractionDigits: 1 })
/** Distancia en línea recta desde el GPS real del conductor hasta el punto marcado; no es ruta ni tiempo de llegada. */
export function formatDistance(metres: number): string {
  if (metres < 1000) return `${Math.max(10, Math.round(metres / 10) * 10)} m`
  return `${kilometres.format(metres / 1000)} km`
}

/** Línea secundaria de una carrera activa: entrega de la orden mientras no hay pasos; después, desde cuándo está en ese paso. */
export function tripProgress(trip: ActiveTrip, time: (iso: string) => string): { text: string; unseen: boolean } {
  if (trip.estado === 'Asignado') return trip.recibidaEn ? { text: `Vista ${time(trip.recibidaEn)}`, unseen: false } : { text: 'Sin confirmar en la app', unseen: true }
  return { text: trip.ultimoPasoEn ? `Desde las ${time(trip.ultimoPasoEn)}` : tripStateLabel[trip.estado], unseen: false }
}

