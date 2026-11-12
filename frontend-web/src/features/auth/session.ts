import { decodeNoContent, request } from '../../services/http.ts'

export type CompanyRole = 'Administrador' | 'Operador' | 'Conductor'
export type Session = {
  usuarioId: string
  email: string | null
  empresaId: string
  roles: CompanyRole[]
  /** Expiración de sesión persistida; no es la expiración del bearer Android. */
  expiraEn: string
}

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
function isId(value: unknown): value is string {
  return typeof value === 'string' && guid.test(value) && value !== '00000000-0000-0000-0000-000000000000'
}
function isRole(value: unknown): value is CompanyRole {
  return value === 'Administrador' || value === 'Operador' || value === 'Conductor'
}

export function decodeSession(body: unknown): Session {
  if (typeof body !== 'object' || body === null ||
    !('usuarioId' in body) || !isId(body.usuarioId) ||
    !('empresaId' in body) || !isId(body.empresaId) ||
    !('email' in body) || !(body.email === null || typeof body.email === 'string') ||
    !('roles' in body) || !Array.isArray(body.roles) || body.roles.length !== 1 || !body.roles.every(isRole) ||
    !('expiraEn' in body) || typeof body.expiraEn !== 'string' || !Number.isFinite(Date.parse(body.expiraEn))) {
    throw new Error('Contrato de sesión inválido.')
  }
  return { usuarioId: body.usuarioId, empresaId: body.empresaId, email: body.email,
    roles: body.roles, expiraEn: body.expiraEn }
}

// La empresa es configuración pública por instalación, nunca un permiso.
export function resolveCompanyId(configured: unknown): string {
  if (!isId(configured)) throw new Error('La instalación no tiene una empresa válida configurada.')
  return configured
}

export function getSession(signal?: AbortSignal): Promise<Session> {
  return request('/api/v1/auth/me', decodeSession, { signal })
}

export class SessionLookupError extends Error {
  constructor(cause: unknown) {
    super('El acceso fue aceptado, pero no se pudo consultar la sesión.', { cause })
    this.name = 'SessionLookupError'
  }
}

export async function loginWeb(email: string, password: string, signal?: AbortSignal): Promise<Session> {
  const empresaId = resolveCompanyId(import.meta.env.VITE_EMPRESA_ID)
  await request('/api/v1/auth/login', (body) => {
    if (typeof body !== 'object' || body === null || !('session' in body) || !isId(body.session) ||
      !('empresaId' in body) || body.empresaId !== empresaId) throw new Error('Respuesta de login inválida.')
  }, { method: 'POST', body: { email, password, empresaId, cliente: 'Web' }, signal })
  // Consultar identidad real: nunca inferir roles a partir de credenciales o configuración local.
  try { return await getSession(signal) }
  catch (error) { throw new SessionLookupError(error) }
}

export function logoutWeb(signal?: AbortSignal): Promise<void> {
  return request('/api/v1/auth/logout', decodeNoContent, { method: 'POST', signal })
}

/** Espacio de cada rol: la central trabaja en la consola; el conductor, en su pantalla de GPS. */
export function homeFor(session: Session | null): string {
  return session?.roles.includes('Conductor') ? '/conductor' : '/solicitudes'
}
