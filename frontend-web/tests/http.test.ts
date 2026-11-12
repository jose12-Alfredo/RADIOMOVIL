import { test } from 'node:test'
import assert from 'node:assert/strict'
import { HttpError, NetworkError, request, decodeNoContent } from '../src/services/http.ts'

test('mutaciones obtienen CSRF fresco y conservan credenciales y clave', async (t) => {
  const calls: RequestInit[] = []
  t.mock.method(globalThis, 'fetch', async (_url: string, init: RequestInit) => {
    calls.push(init)
    return calls.length % 2 === 1
      ? Response.json({ token: `token-${calls.length}` })
      : new Response(null, { status: 204 })
  })
  for (let i = 0; i < 2; i++) await request('/api/v1/auth/logout', decodeNoContent, { method: 'POST', idempotencyKey: 'same-operation' })
  assert.equal(calls[1]?.credentials, 'same-origin')
  assert.equal(new Headers(calls[1]?.headers).get('X-CSRF-TOKEN'), 'token-1')
  assert.equal(new Headers(calls[3]?.headers).get('X-CSRF-TOKEN'), 'token-3')
  assert.equal(new Headers(calls[1]?.headers).get('Idempotency-Key'), 'same-operation')
})

test('errores HTTP conservan status/cuerpo y no reintentan', async (t) => {
  for (const status of [400, 401, 403, 404, 409, 500]) {
    const mock = t.mock.method(globalThis, 'fetch', async () => new Response('error', { status }))
    await assert.rejects(request('/api/v1/solicitudes', x => x), (error: unknown) =>
      error instanceof HttpError && error.status === status && error.body === 'error')
    assert.equal(mock.mock.callCount(), 1)
    mock.mock.restore()
  }
})

test('CSRF inválido impide enviar la mutación', async (t) => {
  const mock = t.mock.method(globalThis, 'fetch', async () => Response.json({ token: null }))
  await assert.rejects(request('/api/v1/auth/logout', decodeNoContent, { method: 'POST' }), /CSRF/)
  assert.equal(mock.mock.callCount(), 1)
})

test('rechaza destinos externos y distingue fallos de red', async (t) => {
  const mock = t.mock.method(globalThis, 'fetch', async () => { throw new TypeError('offline') })
  await assert.rejects(request('https://example.com', x => x), /rutas relativas/)
  assert.equal(mock.mock.callCount(), 0)
  await assert.rejects(request('/api/v1/solicitudes', x => x), NetworkError)
})
