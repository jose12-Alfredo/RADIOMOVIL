export class HttpError extends Error {
  readonly status: number
  readonly body: unknown
  constructor(status: number, body: unknown) {
    super(`La API respondió HTTP ${status}.`)
    this.status = status
    this.body = body
    this.name = 'HttpError'
  }
}

export class NetworkError extends Error {
  constructor(cause: unknown) {
    super('No se pudo completar la comunicación con la API.', { cause })
    this.name = 'NetworkError'
  }
}

type Method = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'
type Options = { method?: Method; body?: unknown; signal?: AbortSignal; idempotencyKey?: string }
type Decoder<T> = (body: unknown) => T

async function send(path: string, options: Options, token?: string): Promise<unknown> {
  if (!path.startsWith('/api/v1/') || path.includes('\\') || path.includes('#')) {
    throw new Error('Solo se permiten rutas relativas /api/v1/.')
  }
  const url = new URL(path, 'https://frontend.invalid')
  if (!url.pathname.startsWith('/api/v1/')) throw new Error('Ruta de API inválida.')
  const headers = new Headers({ Accept: 'application/json' })
  if (options.body !== undefined) headers.set('Content-Type', 'application/json')
  if (token) headers.set('X-CSRF-TOKEN', token)
  if (options.idempotencyKey) headers.set('Idempotency-Key', options.idempotencyKey)
  let response: Response
  try {
    response = await fetch(path, {
      method: options.method ?? 'GET', headers, credentials: 'same-origin', cache: 'no-store',
      redirect: 'error', signal: options.signal,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
    })
  } catch (error) {
    if (options.signal?.aborted) throw error
    throw new NetworkError(error)
  }
  const text = await response.text()
  let body: unknown = text || undefined
  if (text && response.headers.get('content-type')?.includes('json')) {
    try { body = JSON.parse(text) as unknown } catch { /* Preservar cuerpos no JSON, incluso en errores. */ }
  }
  if (!response.ok) throw new HttpError(response.status, body)
  return body
}

export async function getCsrfToken(signal?: AbortSignal): Promise<string> {
  const body = await send('/api/v1/auth/csrf', { signal })
  if (typeof body !== 'object' || body === null || !('token' in body) ||
    typeof body.token !== 'string' || !body.token) throw new Error('Respuesta CSRF inválida.')
  return body.token
}

// Token fresco para cada mutación: nunca reutilizar uno asociado a una identidad anterior.
// No reintentar mutaciones automáticamente ni inferir que una respuesta perdida implica rollback.
export async function request<T>(path: string, decode: Decoder<T>, options: Options = {}): Promise<T> {
  const token = options.method && options.method !== 'GET' ? await getCsrfToken(options.signal) : undefined
  return decode(await send(path, options, token))
}

export function decodeNoContent(body: unknown): void {
  if (body !== undefined) throw new Error('Se esperaba una respuesta sin contenido.')
}
