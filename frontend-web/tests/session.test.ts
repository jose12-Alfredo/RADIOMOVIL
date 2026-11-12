import { test } from 'node:test'
import assert from 'node:assert/strict'
import { decodeSession, getSession, resolveCompanyId } from '../src/features/auth/session.ts'
import { HttpError } from '../src/services/http.ts'

// Datos sintéticos exclusivamente para pruebas unitarias, nunca configuración empresarial.
const identity = { usuarioId: '11111111-1111-1111-1111-111111111111',
  empresaId: '22222222-2222-2222-2222-222222222222', email: 'test@example.invalid',
  roles: ['Operador'], expiraEn: '2026-11-07T00:00:00Z' }

test('valida contrato y rechaza roles, IDs y expiración inválidos', () => {
  assert.deepEqual(decodeSession(identity), identity)
  for (const change of [{ roles: ['SuperAdmin'] }, { roles: [] }, { expiraEn: 'invalid' },
    { usuarioId: '' }, { empresaId: '00000000-0000-0000-0000-000000000000' }]) {
    assert.throws(() => decodeSession({ ...identity, ...change }), /Contrato/)
  }
})

test('empresa sin valor predeterminado: configuración requerida y validada', () => {
  for (const value of [undefined, '', 'empresa', '00000000-0000-0000-0000-000000000000']) {
    assert.throws(() => resolveCompanyId(value), /configurada/)
  }
  assert.equal(resolveCompanyId(identity.empresaId), identity.empresaId)
})

test('consulta sesión por cookie sin enviar empresa ni aceptar autorización inventada', async (t) => {
  t.mock.method(globalThis, 'fetch', async (url: string, init: RequestInit) => {
    assert.equal(url, '/api/v1/auth/me')
    assert.equal(init.credentials, 'same-origin')
    assert.equal(init.body, undefined)
    assert.equal(new Headers(init.headers).get('Authorization'), null)
    return Response.json(identity)
  })
  assert.deepEqual(await getSession(), identity)
})

test('propaga sesión ausente/revocada y membresía denegada, sin fabricar sesión', async (t) => {
  for (const status of [401, 403]) {
    const mock = t.mock.method(globalThis, 'fetch', async () => new Response(null, { status }))
    await assert.rejects(getSession(), (error: unknown) => error instanceof HttpError && error.status === status)
    mock.mock.restore()
  }
})
