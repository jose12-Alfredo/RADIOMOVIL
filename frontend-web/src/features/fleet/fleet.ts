import { HttpError, NetworkError, request } from '../../services/http.ts'
import { isGuid } from '../requests/requests.ts'

// Contratos de /api/v1/conductores, /vehiculos y /turnos (Fase B). El estado lo calcula
// el servidor: React no decide si un conductor está disponible, solo lo muestra.
export const driverStates = ['Disponible', 'Ocupado', 'FueraDeServicio'] as const
export type DriverState = typeof driverStates[number]
export const driverStateLabel: Record<DriverState, string> = {
  Disponible: 'Disponible', Ocupado: 'Ocupado', FueraDeServicio: 'Fuera de servicio',
}
export type Occupation = 'Oferta' | 'Viaje'
export const shiftsPageSize = 20
export const driverNameMaxLength = 160
export const closeReasonMaxLength = 500

export type ActiveShift = { id: string; vehiculoId: string; patente: string; inicio: string }
export type Driver = { id: string; nombre: string; estado: DriverState; ocupacion: Occupation | null; turno: ActiveShift | null; accesoEmail: string | null }
export type Vehicle = { id: string; patente: string; turnoId: string | null; conductorId: string | null; conductorNombre: string | null }
export type Shift = { id: string; conductorId: string; conductorNombre: string; vehiculoId: string; patente: string;
  inicio: string; fin: string | null; motivoCierre: string | null }
export type ShiftPage = { page: number; pageSize: number; total: number; items: Shift[] }
export type OperationResult = { id: string; repetida: boolean }

function record(body: unknown, message: string): Record<string, unknown> {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) throw new Error(message)
  return body as Record<string, unknown>
}
const isDate = (value: unknown): value is string => typeof value === 'string' && Number.isFinite(Date.parse(value))
const optionalText = (value: unknown) => value === undefined || value === null || typeof value === 'string'

function decodeActiveShift(body: unknown): ActiveShift {
  const value = record(body, 'Contrato de turno inválido.')
  if (!isGuid(value.id) || !isGuid(value.vehiculoId) || typeof value.patente !== 'string' || !isDate(value.inicio)) throw new Error('Contrato de turno inválido.')
  return { id: value.id, vehiculoId: value.vehiculoId, patente: value.patente, inicio: value.inicio }
}

export function decodeDrivers(body: unknown): Driver[] {
  if (!Array.isArray(body)) throw new Error('Contrato de conductores inválido.')
  return body.map(item => {
    const value = record(item, 'Contrato de conductores inválido.')
    const estado = driverStates.find(state => state === value.estado)
    const ocupacion = value.ocupacion === 'Oferta' || value.ocupacion === 'Viaje' ? value.ocupacion : null
    if (!isGuid(value.id) || typeof value.nombre !== 'string' || estado === undefined ||
      (value.ocupacion !== null && value.ocupacion !== undefined && ocupacion === null) ||
      (estado === 'Ocupado') !== (ocupacion !== null)) throw new Error('Contrato de conductores inválido.')
    const turno = value.turno === null || value.turno === undefined ? null : decodeActiveShift(value.turno)
    if ((estado === 'Disponible' && turno === null) || !optionalText(value.accesoEmail)) throw new Error('Contrato de conductores inválido.')
    return { id: value.id, nombre: value.nombre, estado, ocupacion, turno, accesoEmail: value.accesoEmail as string | null ?? null }
  })
}

export function decodeVehicles(body: unknown): Vehicle[] {
  if (!Array.isArray(body)) throw new Error('Contrato de vehículos inválido.')
  return body.map(item => {
    const value = record(item, 'Contrato de vehículos inválido.')
    const inShift = value.turnoId !== null && value.turnoId !== undefined
    if (!isGuid(value.id) || typeof value.patente !== 'string' || (inShift && (!isGuid(value.turnoId) || !isGuid(value.conductorId))) ||
      !optionalText(value.conductorNombre)) throw new Error('Contrato de vehículos inválido.')
    return { id: value.id, patente: value.patente, turnoId: inShift ? value.turnoId as string : null,
      conductorId: inShift ? value.conductorId as string : null, conductorNombre: inShift ? value.conductorNombre as string | null ?? null : null }
  })
}

export function decodeShiftPage(body: unknown): ShiftPage {
  const value = record(body, 'Contrato de turnos inválido.')
  if (!Number.isInteger(value.page) || !Number.isInteger(value.pageSize) || !Number.isInteger(value.total) || !Array.isArray(value.items)) throw new Error('Contrato de turnos inválido.')
  return { page: value.page as number, pageSize: value.pageSize as number, total: value.total as number, items: value.items.map(item => {
    const shift = record(item, 'Contrato de turnos inválido.')
    if (!isGuid(shift.id) || !isGuid(shift.conductorId) || typeof shift.conductorNombre !== 'string' || !isGuid(shift.vehiculoId) ||
      typeof shift.patente !== 'string' || !isDate(shift.inicio) || !(shift.fin === null || shift.fin === undefined || isDate(shift.fin)) ||
      !optionalText(shift.motivoCierre)) throw new Error('Contrato de turnos inválido.')
    return { id: shift.id, conductorId: shift.conductorId, conductorNombre: shift.conductorNombre, vehiculoId: shift.vehiculoId,
      patente: shift.patente, inicio: shift.inicio, fin: shift.fin as string | null ?? null, motivoCierre: shift.motivoCierre as string | null ?? null }
  }) }
}

export function decodeOperation(body: unknown): OperationResult {
  const value = record(body, 'Respuesta de operación inválida.')
  if (!isGuid(value.id) || typeof value.repetida !== 'boolean') throw new Error('Respuesta de operación inválida.')
  return { id: value.id, repetida: value.repetida }
}

export const listDrivers = (signal?: AbortSignal) => request('/api/v1/conductores', decodeDrivers, { signal })
export const listVehicles = (signal?: AbortSignal) => request('/api/v1/vehiculos', decodeVehicles, { signal })

export function listShifts(filter: { abiertos?: boolean; page?: number }, signal?: AbortSignal): Promise<ShiftPage> {
  const query = new URLSearchParams({ page: String(filter.page ?? 1), pageSize: String(shiftsPageSize) })
  if (filter.abiertos !== undefined) query.set('abiertos', String(filter.abiertos))
  return request(`/api/v1/turnos?${query}`, decodeShiftPage, { signal })
}

export const registerDriver = (input: { nombre: string }, key: string) =>
  request('/api/v1/conductores', decodeOperation, { method: 'POST', body: input, idempotencyKey: key })
export const registerVehicle = (input: { patente: string }, key: string) =>
  request('/api/v1/vehiculos', decodeOperation, { method: 'POST', body: input, idempotencyKey: key })
export const openShift = (input: { conductorId: string; vehiculoId: string }, key: string) =>
  request('/api/v1/turnos', decodeOperation, { method: 'POST', body: input, idempotencyKey: key })
export const closeShift = (input: { turnoId: string; motivo: string | null }, key: string) =>
  request(`/api/v1/turnos/${encodeURIComponent(input.turnoId)}/cerrar`, decodeOperation,
    { method: 'POST', body: { motivo: input.motivo }, idempotencyKey: key })

/** Misma regla que el backend, solo para avisar antes de enviar; el servidor decide. */
export function normalizePlate(value: string): string {
  return value.replace(/\s+/g, '').toUpperCase()
}
export function validatePlate(value: string): string | null {
  const plate = normalizePlate(value)
  if (!plate) return 'Escribe la placa del vehículo.'
  if (!/^[A-Z0-9-]{2,16}$/.test(plate)) return 'La placa admite de 2 a 16 letras, números o guiones.'
  return null
}
export function validateDriverName(value: string): string | null {
  const name = value.trim()
  if (!name) return 'Escribe el nombre del conductor.'
  if (name.length > driverNameMaxLength) return `El nombre no puede superar los ${driverNameMaxLength} caracteres.`
  return null
}

export type ActionFailure =
  /** El servidor pudo haber aplicado la operación: conservar clave y datos para reintentar sin duplicar. */
  | { kind: 'uncertain'; message: string }
  | { kind: 'rejected'; message: string }
  | { kind: 'session'; message: string }

export function classifyActionError(error: unknown): ActionFailure {
  if (error instanceof NetworkError) return { kind: 'uncertain', message: 'Se perdió la comunicación con el servidor.' }
  if (error instanceof HttpError) {
    const detail = typeof error.body === 'string' && error.body.trim() ? error.body.trim() : null
    if (error.status === 401) return { kind: 'session', message: 'Tu sesión ya no está disponible. Vuelve a iniciar sesión.' }
    if (error.status === 403) return { kind: 'rejected', message: 'Tu cuenta no tiene permiso para esta operación.' }
    if (error.status === 404) return { kind: 'rejected', message: detail ?? 'No se encontró en tu empresa.' }
    if (error.status === 409) return { kind: 'rejected', message: detail ?? 'La operación ya no es compatible con el estado actual.' }
    if (error.status === 400) return { kind: 'rejected', message: detail ?? 'El servidor rechazó los datos.' }
    if (error.status >= 500) return { kind: 'uncertain', message: 'El servidor falló al procesar la operación.' }
    return { kind: 'rejected', message: 'El servidor rechazó la operación.' }
  }
  return { kind: 'uncertain', message: 'No se pudo interpretar la respuesta del servidor.' }
}

export function describeFleetLoadError(error: unknown): string {
  if (error instanceof NetworkError) return 'No pudimos conectar con el servidor.'
  if (error instanceof HttpError) {
    if (error.status === 403) return 'Tu cuenta no tiene permiso para ver la flota.'
    if (error.status === 400) return 'El servidor rechazó la consulta.'
    if (error.status >= 500) return 'El servicio no está disponible en este momento.'
  }
  return 'No pudimos interpretar la respuesta del servidor.'
}

// ---- Fase C: acceso del conductor, vehículos autorizados y ubicaciones ----
export type Authorization = { id: string; conductorId: string; conductorNombre: string; vehiculoId: string; patente: string; vehiculoLibre: boolean; autorizadaEn: string }

export function decodeAuthorizations(body: unknown): Authorization[] {
  if (!Array.isArray(body)) throw new Error('Contrato de autorizaciones inválido.')
  return body.map(item => {
    const value = record(item, 'Contrato de autorizaciones inválido.')
    if (!isGuid(value.id) || !isGuid(value.conductorId) || typeof value.conductorNombre !== 'string' || !isGuid(value.vehiculoId) ||
      typeof value.patente !== 'string' || typeof value.vehiculoLibre !== 'boolean' || !isDate(value.autorizadaEn)) throw new Error('Contrato de autorizaciones inválido.')
    return { id: value.id, conductorId: value.conductorId, conductorNombre: value.conductorNombre, vehiculoId: value.vehiculoId,
      patente: value.patente, vehiculoLibre: value.vehiculoLibre, autorizadaEn: value.autorizadaEn }
  })
}

export const listAuthorizations = (conductorId: string, signal?: AbortSignal) =>
  request(`/api/v1/autorizaciones-vehiculo?conductorId=${encodeURIComponent(conductorId)}`, decodeAuthorizations, { signal })
export const authorizeVehicle = (input: { conductorId: string; vehiculoId: string }, key: string) =>
  request('/api/v1/autorizaciones-vehiculo', decodeOperation, { method: 'POST', body: input, idempotencyKey: key })
export const revokeAuthorization = (input: { autorizacionId: string }, key: string) =>
  request(`/api/v1/autorizaciones-vehiculo/${encodeURIComponent(input.autorizacionId)}/revocar`, decodeOperation, { method: 'POST', body: {}, idempotencyKey: key })

/** Alta de acceso con el endpoint existente de usuarios (rol Conductor). La contraseña nunca se guarda en el navegador. */
export async function createDriverAccess(input: { conductorId: string; email: string; password: string }): Promise<void> {
  await request('/api/v1/auth/usuarios', body => {
    const value = record(body, 'Respuesta de acceso inválida.')
    if (!isGuid(value.id)) throw new Error('Respuesta de acceso inválida.')
  }, { method: 'POST', body: { email: input.email, password: input.password, rol: 'Conductor', conductorId: input.conductorId } })
}

/** Mismas reglas que Identity en el backend; el servidor decide. */
export function validateAccessPassword(value: string): string | null {
  if (value.length < 12) return 'La contraseña inicial necesita al menos 12 caracteres.'
  if (!/[A-Z]/.test(value) || !/[0-9]/.test(value) || !/[^A-Za-z0-9]/.test(value)) return 'Incluye una mayúscula, un número y un símbolo.'
  return null
}

export const gpsStates = ['Reciente', 'Desactualizado', 'SinSenal'] as const
export type GpsState = typeof gpsStates[number]
export const gpsStateLabel: Record<GpsState, string> = { Reciente: 'GPS reciente', Desactualizado: 'GPS desactualizado', SinSenal: 'Sin señal' }
export type DriverLocation = { conductorId: string; nombre: string; turnoId: string; patente: string; latitud: number | null; longitud: number | null;
  precisionMetros: number | null; capturadaEn: string | null; recibidaEn: string | null; antiguedadSegundos: number | null; estadoGps: GpsState }
export type LocationSnapshot = { servidorAhora: string; recienteSegundos: number; desactualizadoSegundos: number; items: DriverLocation[] }

const optionalNumber = (value: unknown) => value === undefined || value === null || (typeof value === 'number' && Number.isFinite(value))

export function decodeLocations(body: unknown): LocationSnapshot {
  const value = record(body, 'Contrato de ubicaciones inválido.')
  const thresholds = record(value.umbrales, 'Contrato de ubicaciones inválido.')
  if (!isDate(value.servidorAhora) || !Number.isInteger(thresholds.recienteSegundos) || !Number.isInteger(thresholds.desactualizadoSegundos) || !Array.isArray(value.items))
    throw new Error('Contrato de ubicaciones inválido.')
  return { servidorAhora: value.servidorAhora, recienteSegundos: thresholds.recienteSegundos as number, desactualizadoSegundos: thresholds.desactualizadoSegundos as number,
    items: value.items.map(item => {
      const row = record(item, 'Contrato de ubicaciones inválido.')
      const estadoGps = gpsStates.find(state => state === row.estadoGps)
      const hasPoint = typeof row.latitud === 'number' && typeof row.longitud === 'number'
      if (!isGuid(row.conductorId) || typeof row.nombre !== 'string' || !isGuid(row.turnoId) || typeof row.patente !== 'string' || estadoGps === undefined ||
        !optionalNumber(row.latitud) || !optionalNumber(row.longitud) || !optionalNumber(row.precisionMetros) || !optionalNumber(row.antiguedadSegundos) ||
        !(row.capturadaEn == null || isDate(row.capturadaEn)) || !(row.recibidaEn == null || isDate(row.recibidaEn)) ||
        (hasPoint && (Math.abs(row.latitud as number) > 90 || Math.abs(row.longitud as number) > 180)) ||
        (estadoGps !== 'SinSenal' && (!hasPoint || row.recibidaEn == null))) throw new Error('Contrato de ubicaciones inválido.')
      return { conductorId: row.conductorId, nombre: row.nombre, turnoId: row.turnoId, patente: row.patente,
        latitud: hasPoint ? row.latitud as number : null, longitud: hasPoint ? row.longitud as number : null,
        precisionMetros: row.precisionMetros as number | null ?? null, capturadaEn: row.capturadaEn as string | null ?? null,
        recibidaEn: row.recibidaEn as string | null ?? null, antiguedadSegundos: row.antiguedadSegundos as number | null ?? null, estadoGps }
    }) }
}

export const listLocations = (signal?: AbortSignal) => request('/api/v1/ubicaciones', decodeLocations, { signal })

/** "hace 12 s", "hace 3 min", "hace 2 h": antigüedad calculada por el servidor. */
export function formatAge(seconds: number): string {
  if (seconds < 60) return `hace ${seconds} s`
  if (seconds < 3600) return `hace ${Math.floor(seconds / 60)} min`
  return `hace ${Math.floor(seconds / 3600)} h`
}
