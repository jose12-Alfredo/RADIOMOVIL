import { HttpError, NetworkError, request } from '../../services/http.ts'
import { isGuid } from '../requests/requests.ts'
import { decodeOperation, driverStates, gpsStates } from '../fleet/fleet.ts'
import type { DriverState, GpsState, Occupation } from '../fleet/fleet.ts'

// Contratos /api/v1/conductor/* (los mismos que usará la app Flutter, aquí con cookie + CSRF).
export type MyShift = { id: string; vehiculoId: string; patente: string; inicio: string }
export type Me = { conductorId: string; nombre: string; estado: DriverState; ocupacion: Occupation | null; turno: MyShift | null
  estadoGps: GpsState; ultimaRecepcion: string | null; intervaloEnvioSegundos: number }
export type MyVehicle = { vehiculoId: string; patente: string; libre: boolean }
export type GpsReceipt = { aceptada: boolean; motivo: string | null; recibidaEn: string; estadoGps: GpsState; intervaloMinimoSegundos: number }

function record(body: unknown, message: string): Record<string, unknown> {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) throw new Error(message)
  return body as Record<string, unknown>
}
const isDate = (value: unknown): value is string => typeof value === 'string' && Number.isFinite(Date.parse(value))

export function decodeMe(body: unknown): Me {
  const value = record(body, 'Contrato de conductor inválido.')
  const estado = driverStates.find(state => state === value.estado)
  const estadoGps = gpsStates.find(state => state === value.estadoGps)
  const ocupacion = value.ocupacion === 'Oferta' || value.ocupacion === 'Viaje' ? value.ocupacion : null
  let turno: MyShift | null = null
  if (value.turno != null) {
    const shift = record(value.turno, 'Contrato de conductor inválido.')
    if (!isGuid(shift.id) || !isGuid(shift.vehiculoId) || typeof shift.patente !== 'string' || !isDate(shift.inicio)) throw new Error('Contrato de conductor inválido.')
    turno = { id: shift.id, vehiculoId: shift.vehiculoId, patente: shift.patente, inicio: shift.inicio }
  }
  if (!isGuid(value.conductorId) || typeof value.nombre !== 'string' || estado === undefined || estadoGps === undefined ||
    !(value.ultimaRecepcion == null || isDate(value.ultimaRecepcion)) || !Number.isInteger(value.intervaloEnvioSegundos) ||
    (value.intervaloEnvioSegundos as number) < 1) throw new Error('Contrato de conductor inválido.')
  return { conductorId: value.conductorId, nombre: value.nombre, estado, ocupacion, turno, estadoGps,
    ultimaRecepcion: value.ultimaRecepcion as string | null ?? null, intervaloEnvioSegundos: value.intervaloEnvioSegundos as number }
}

export function decodeMyVehicles(body: unknown): MyVehicle[] {
  if (!Array.isArray(body)) throw new Error('Contrato de vehículos inválido.')
  return body.map(item => {
    const value = record(item, 'Contrato de vehículos inválido.')
    if (!isGuid(value.vehiculoId) || typeof value.patente !== 'string' || typeof value.libre !== 'boolean') throw new Error('Contrato de vehículos inválido.')
    return { vehiculoId: value.vehiculoId, patente: value.patente, libre: value.libre }
  })
}

export function decodeReceipt(body: unknown): GpsReceipt {
  const value = record(body, 'Respuesta de ubicación inválida.')
  const estadoGps = gpsStates.find(state => state === value.estadoGps)
  if (typeof value.aceptada !== 'boolean' || !(value.motivo == null || typeof value.motivo === 'string') || !isDate(value.recibidaEn) ||
    estadoGps === undefined || !Number.isInteger(value.intervaloMinimoSegundos)) throw new Error('Respuesta de ubicación inválida.')
  return { aceptada: value.aceptada, motivo: value.motivo as string | null ?? null, recibidaEn: value.recibidaEn, estadoGps,
    intervaloMinimoSegundos: value.intervaloMinimoSegundos as number }
}

export const getMe = (signal?: AbortSignal) => request('/api/v1/conductor/yo', decodeMe, { signal })
export const listMyVehicles = (signal?: AbortSignal) => request('/api/v1/conductor/vehiculos', decodeMyVehicles, { signal })
export const startMyShift = (input: { vehiculoId: string }, key: string) =>
  request('/api/v1/conductor/turno', decodeOperation, { method: 'POST', body: input, idempotencyKey: key })
export const closeMyShift = (input: { motivo: string | null }, key: string) =>
  request('/api/v1/conductor/turno/cerrar', decodeOperation, { method: 'POST', body: input, idempotencyKey: key })

export type Fix = { latitud: number; longitud: number; precisionMetros: number; capturadaEn: string }

/** Solo convierte una lectura REAL de la Geolocation API; nunca fabrica coordenadas. */
export function fixFromPosition(position: { coords: { latitude: number; longitude: number; accuracy: number }; timestamp: number }): Fix | null {
  const { latitude, longitude, accuracy } = position.coords
  if (![latitude, longitude, accuracy, position.timestamp].every(Number.isFinite) || accuracy <= 0) return null
  return { latitud: latitude, longitud: longitude, precisionMetros: accuracy, capturadaEn: new Date(position.timestamp).toISOString() }
}

export const sendFix = (fix: Fix) => request('/api/v1/conductor/ubicaciones', decodeReceipt, { method: 'POST', body: fix })

export type SendOutcome =
  | { kind: 'sent'; receipt: GpsReceipt }
  | { kind: 'wait'; seconds: number }
  | { kind: 'no-shift'; message: string }
  | { kind: 'session' }
  | { kind: 'rejected'; message: string }
  | { kind: 'offline'; message: string }

export function classifySendError(error: unknown): SendOutcome {
  if (error instanceof NetworkError) return { kind: 'offline', message: 'Sin conexión con el servidor. Se reintentará con la próxima lectura.' }
  if (error instanceof HttpError) {
    const detail = typeof error.body === 'string' && error.body.trim() ? error.body.trim() : null
    if (error.status === 401) return { kind: 'session' }
    if (error.status === 409) return { kind: 'no-shift', message: detail ?? 'No tienes un turno abierto.' }
    if (error.status === 429) return { kind: 'wait', seconds: 5 }
    if (error.status === 403) return { kind: 'rejected', message: 'Tu cuenta no puede enviar ubicaciones.' }
    if (error.status === 400) return { kind: 'rejected', message: detail ?? 'El servidor rechazó la lectura.' }
    if (error.status >= 500) return { kind: 'offline', message: 'El servidor falló. Se reintentará con la próxima lectura.' }
  }
  return { kind: 'offline', message: 'No se pudo interpretar la respuesta. Se reintentará.' }
}

export function geolocationError(code: number): string {
  if (code === 1) return 'Permiso de ubicación denegado. Actívalo para este sitio en los ajustes del navegador y vuelve a intentarlo.'
  if (code === 2) return 'El teléfono no puede determinar tu ubicación. Activa el GPS y sal a un lugar con buena señal.'
  if (code === 3) return 'El GPS tardó demasiado en responder. Seguimos intentándolo.'
  return 'No se pudo leer la ubicación.'
}
