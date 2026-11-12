import { request } from '../../services/http.ts'
import { HttpError, NetworkError } from '../../services/http.ts'

// Orden del enum EstadoSolicitud del backend: la API lo serializa como número.
export const requestStates = ['Pendiente', 'Ofertada', 'Asignada', 'EnViaje', 'Finalizada', 'Cancelada'] as const
export type RequestState = typeof requestStates[number]
export const requestStateLabel: Record<RequestState, string> = {
  Pendiente: 'Pendiente', Ofertada: 'Ofertada', Asignada: 'Asignada', EnViaje: 'En viaje', Finalizada: 'Finalizada', Cancelada: 'Cancelada',
}
export const originMaxLength = 500
export const pageSize = 20

export type ServiceRequest = {
  id: string
  empresaId: string
  centralId: string
  origen: string
  estado: RequestState
  creadaEn: string
  ordenRecepcion: number
  telefonoPasajero: string | null
  nombrePasajero: string | null
  destino: string | null
  observaciones: string | null
  latitud: number | null
  longitud: number | null
  destinoLatitud: number | null
  destinoLongitud: number | null
}
export type Central = { id: string; nombre: string }
export type CreateRequestInput = { centralId: string; origen: string; telefonoPasajero?: string | null;
  nombrePasajero?: string | null; destino?: string | null; observaciones?: string | null;
  latitud?: number | null; longitud?: number | null; destinoLatitud?: number | null; destinoLongitud?: number | null }
export type RequestPage = { page: number; pageSize: number; total: number; items: ServiceRequest[] }
export type DispatchCandidate = { conductorId: string; nombre: string; ubicacionVerificada: boolean }

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
export function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guid.test(value) && value !== '00000000-0000-0000-0000-000000000000'
}

function record(body: unknown, message: string): Record<string, unknown> {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) throw new Error(message)
  return body as Record<string, unknown>
}

export function decodeRequest(body: unknown): ServiceRequest {
  const value = record(body, 'Contrato de solicitud inválido.')
  const estado = typeof value.estado === 'number' ? requestStates[value.estado] : undefined
  if (!isGuid(value.id) || !isGuid(value.empresaId) || !isGuid(value.centralId) ||
    typeof value.origen !== 'string' || estado === undefined ||
    typeof value.creadaEn !== 'string' || !Number.isFinite(Date.parse(value.creadaEn)) ||
    typeof value.ordenRecepcion !== 'number' || !Number.isInteger(value.ordenRecepcion)) {
    throw new Error('Contrato de solicitud inválido.')
  }
  for (const key of ['telefonoPasajero', 'nombrePasajero', 'destino', 'observaciones']) {
    if (value[key] !== undefined && value[key] !== null && typeof value[key] !== 'string') throw new Error('Contrato de solicitud inválido.')
  }
  for (const key of ['latitud', 'longitud', 'destinoLatitud', 'destinoLongitud']) {
    if (value[key] !== undefined && value[key] !== null && (typeof value[key] !== 'number' || !Number.isFinite(value[key]))) throw new Error('Contrato de solicitud inválido.')
  }
  return { id: value.id, empresaId: value.empresaId, centralId: value.centralId, origen: value.origen, estado,
    creadaEn: value.creadaEn, ordenRecepcion: value.ordenRecepcion,
    telefonoPasajero: value.telefonoPasajero as string | null ?? null,
    nombrePasajero: value.nombrePasajero as string | null ?? null,
    destino: value.destino as string | null ?? null, observaciones: value.observaciones as string | null ?? null,
    latitud: value.latitud as number | null ?? null, longitud: value.longitud as number | null ?? null,
    destinoLatitud: value.destinoLatitud as number | null ?? null, destinoLongitud: value.destinoLongitud as number | null ?? null }
}

export function decodeCentrales(body: unknown): Central[] {
  if (!Array.isArray(body)) throw new Error('Contrato de centrales inválido.')
  return body.map(item => {
    const value = record(item, 'Contrato de centrales inválido.')
    if (!isGuid(value.id) || typeof value.nombre !== 'string' || !value.nombre.trim()) throw new Error('Contrato de centrales inválido.')
    return { id: value.id, nombre: value.nombre }
  })
}

export function listCentrales(signal?: AbortSignal): Promise<Central[]> {
  return request('/api/v1/centrales', decodeCentrales, { signal })
}

export function decodeRequestPage(body: unknown): RequestPage {
  const value = record(body, 'Contrato de listado inválido.')
  if (!Number.isInteger(value.page) || !Number.isInteger(value.pageSize) || !Number.isInteger(value.total) || !Array.isArray(value.items)) {
    throw new Error('Contrato de listado inválido.')
  }
  return { page: value.page as number, pageSize: value.pageSize as number, total: value.total as number, items: value.items.map(decodeRequest) }
}

export function decodeCandidates(body: unknown): DispatchCandidate[] {
  if (!Array.isArray(body)) throw new Error('Contrato de candidatos inválido.')
  return body.map((item: unknown) => {
    const value = record(item, 'Contrato de candidatos inválido.')
    if (!isGuid(value.conductorId) || typeof value.nombre !== 'string' || typeof value.ubicacionVerificada !== 'boolean') {
      throw new Error('Contrato de candidatos inválido.')
    }
    return { conductorId: value.conductorId, nombre: value.nombre, ubicacionVerificada: value.ubicacionVerificada }
  })
}

export function listRequests(filter: { estado?: RequestState; page?: number; pageSize?: number }, signal?: AbortSignal): Promise<RequestPage> {
  const query = new URLSearchParams({ page: String(filter.page ?? 1), pageSize: String(filter.pageSize ?? pageSize) })
  if (filter.estado) query.set('estado', filter.estado)
  return request(`/api/v1/solicitudes?${query}`, decodeRequestPage, { signal })
}

export function getRequest(id: string, signal?: AbortSignal): Promise<ServiceRequest> {
  return request(`/api/v1/solicitudes/${encodeURIComponent(id)}`, decodeRequest, { signal })
}

export function createRequest(input: CreateRequestInput, idempotencyKey: string, signal?: AbortSignal): Promise<ServiceRequest> {
  return request('/api/v1/solicitudes', decodeRequest, { method: 'POST', body: input, idempotencyKey, signal })
}

export function listCandidates(signal?: AbortSignal): Promise<DispatchCandidate[]> {
  return request('/api/v1/despacho/candidatos', decodeCandidates, { signal })
}

export type StateCounts = Record<RequestState, number>

// El listado devuelve `total` para cada filtro: la cifra siempre procede del servidor.
export async function countByState(signal?: AbortSignal): Promise<StateCounts> {
  const totals = await Promise.all(requestStates.map(estado => listRequests({ estado, page: 1, pageSize: 1 }, signal)))
  return Object.fromEntries(requestStates.map((estado, index) => [estado, totals[index].total])) as StateCounts
}

export function resolveCentralId(configured: unknown): string | null {
  return isGuid(configured) ? configured : null
}

export function validateOrigin(origen: string): string | null {
  const value = origen.trim()
  if (!value) return 'Escribe el origen de la solicitud.'
  if (value.length > originMaxLength) return `El origen no puede superar los ${originMaxLength} caracteres.`
  return null
}

export type CreateFailure =
  /** El servidor pudo haber confirmado: conservar clave y contenido para reintentar sin duplicar. */
  | { kind: 'uncertain'; message: string }
  /** El servidor rechazó la petición: nada se registró y se puede corregir. */
  | { kind: 'rejected'; message: string }
  | { kind: 'session'; message: string }

export function classifyCreateError(error: unknown): CreateFailure {
  if (error instanceof NetworkError) return { kind: 'uncertain', message: 'Se perdió la comunicación con el servidor.' }
  if (error instanceof HttpError) {
    if (error.status === 401) return { kind: 'session', message: 'Tu sesión ya no está disponible. Vuelve a iniciar sesión.' }
    if (error.status === 403) return { kind: 'rejected', message: 'Tu cuenta no tiene permiso para registrar solicitudes.' }
    if (error.status === 409) return { kind: 'rejected', message: 'Esta solicitud ya se envió con otros datos. Revisa la cola antes de registrarla de nuevo.' }
    if (error.status === 400) return { kind: 'rejected', message: typeof error.body === 'string' && error.body ? error.body : 'El servidor rechazó los datos de la solicitud.' }
    if (error.status >= 500) return { kind: 'uncertain', message: 'El servidor falló al procesar la solicitud.' }
    return { kind: 'rejected', message: 'El servidor rechazó la solicitud.' }
  }
  // Respuesta que no se pudo interpretar: el servidor pudo haber confirmado.
  return { kind: 'uncertain', message: 'No se pudo interpretar la respuesta del servidor.' }
}

export function describeLoadError(error: unknown): string {
  if (error instanceof NetworkError) return 'No pudimos conectar con el servidor.'
  if (error instanceof HttpError) {
    if (error.status === 403) return 'Tu cuenta no tiene permiso para ver esta información.'
    if (error.status === 404) return 'No encontramos esta solicitud en tu empresa.'
    if (error.status === 400) return 'El servidor rechazó la consulta.'
    if (error.status >= 500) return 'El servicio no está disponible en este momento.'
  }
  return 'No pudimos interpretar la respuesta del servidor.'
}
