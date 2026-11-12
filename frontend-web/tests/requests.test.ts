import assert from 'node:assert/strict'
import test from 'node:test'
import { HttpError, NetworkError } from '../src/services/http.ts'
import { classifyCreateError, createRequest, decodeCandidates, decodeCentrales, decodeRequest, decodeRequestPage, describeLoadError, listCentrales, listRequests, resolveCentralId, validateOrigin } from '../src/features/requests/requests.ts'

const id = '11111111-1111-4111-8111-111111111111'
const wire = { id, empresaId: id, centralId: id, origen: 'Origen de prueba', estado: 3, creadaEn: '2026-11-06T19:00:00Z', ordenRecepcion: 7 }

test('decodifica el estado numérico del backend', () => {
  assert.equal(decodeRequest(wire).estado, 'EnViaje')
  assert.equal(decodeRequest({ ...wire, estado: 0 }).estado, 'Pendiente')
})

test('rechaza contratos inválidos en lugar de mostrar datos dudosos', () => {
  assert.throws(() => decodeRequest({ ...wire, estado: 6 }))
  assert.throws(() => decodeRequest({ ...wire, estado: 'Pendiente' }))
  assert.throws(() => decodeRequest({ ...wire, id: 'x' }))
  assert.throws(() => decodeRequest({ ...wire, creadaEn: 'ayer' }))
  assert.throws(() => decodeRequestPage({ page: 1, pageSize: 20, items: [] }))
  assert.throws(() => decodeCandidates([{ conductorId: id, nombre: 'A' }]))
})

test('decodifica página y candidatos', () => {
  const page = decodeRequestPage({ page: 2, pageSize: 20, total: 21, items: [wire] })
  assert.equal(page.total, 21)
  assert.equal(page.items[0].ordenRecepcion, 7)
  assert.deepEqual(decodeCandidates([{ conductorId: id, nombre: 'A', ubicacionVerificada: false }])[0].ubicacionVerificada, false)
})

test('centrales proceden del backend y datos de llamada conservan coordenadas opcionales', async () => {
  assert.deepEqual(decodeCentrales([{ id, nombre: 'Central Uno' }]), [{ id, nombre: 'Central Uno' }])
  assert.throws(() => decodeCentrales([{ id: 'no-guid', nombre: 'x' }]))
  assert.equal(decodeRequest({ ...wire, latitud: -16.5, longitud: -68.15 }).latitud, -16.5)
  const stub = stubFetch(() => json([{ id, nombre: 'Central Uno' }]))
  try { assert.equal((await listCentrales())[0].id, id); assert.equal(stub.calls[0].url, '/api/v1/centrales') }
  finally { stub.restore() }
})

test('una central solo se acepta como GUID configurado, nunca por defecto', () => {
  assert.equal(resolveCentralId(undefined), null)
  assert.equal(resolveCentralId(''), null)
  assert.equal(resolveCentralId('00000000-0000-0000-0000-000000000000'), null)
  assert.equal(resolveCentralId(id), id)
})

test('valida el origen con los límites de la API', () => {
  assert.ok(validateOrigin('   '))
  assert.ok(validateOrigin('x'.repeat(501)))
  assert.equal(validateOrigin('  ' + 'x'.repeat(500) + '  '), null)
})

test('un resultado dudoso conserva el reintento; un rechazo no', () => {
  assert.equal(classifyCreateError(new NetworkError(new Error('caída'))).kind, 'uncertain')
  assert.equal(classifyCreateError(new HttpError(500, undefined)).kind, 'uncertain')
  assert.equal(classifyCreateError(new Error('contrato')).kind, 'uncertain')
  assert.equal(classifyCreateError(new HttpError(409, 'x')).kind, 'rejected')
  assert.equal(classifyCreateError(new HttpError(403, undefined)).kind, 'rejected')
  assert.equal(classifyCreateError(new HttpError(401, undefined)).kind, 'session')
  assert.equal(classifyCreateError(new HttpError(400, 'CentralId y Origen son obligatorios.')).message, 'CentralId y Origen son obligatorios.')
})

test('describe errores de carga por estado', () => {
  assert.match(describeLoadError(new HttpError(404, undefined)), /No encontramos/)
  assert.match(describeLoadError(new HttpError(403, undefined)), /permiso/)
  assert.match(describeLoadError(new NetworkError(new Error('x'))), /conectar/)
})

function stubFetch(handler: (url: string, init: RequestInit) => Response) {
  const calls: { url: string; init: RequestInit }[] = []
  const original = globalThis.fetch
  globalThis.fetch = (async (input: string, init: RequestInit) => {
    calls.push({ url: input, init })
    return handler(input, init)
  }) as typeof fetch
  return { calls, restore: () => { globalThis.fetch = original } }
}
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

test('el listado envía filtros reales de la API', async () => {
  const stub = stubFetch(() => json({ page: 3, pageSize: 20, total: 0, items: [] }))
  try {
    await listRequests({ estado: 'EnViaje', page: 3 })
    assert.equal(stub.calls[0].url, '/api/v1/solicitudes?page=3&pageSize=20&estado=EnViaje')
  } finally { stub.restore() }
})

test('registrar envía clave idempotente y token CSRF', async () => {
  const stub = stubFetch(url => url.endsWith('/auth/csrf') ? json({ token: 'csrf' }) : json(wire, 201))
  try {
    const created = await createRequest({ centralId: id, origen: 'Origen de prueba' }, 'clave-1')
    const post = stub.calls[1]
    const headers = new Headers(post.init.headers)
    assert.equal(post.init.method, 'POST')
    assert.equal(headers.get('Idempotency-Key'), 'clave-1')
    assert.equal(headers.get('X-CSRF-TOKEN'), 'csrf')
    assert.deepEqual(JSON.parse(String(post.init.body)), { centralId: id, origen: 'Origen de prueba' })
    assert.equal(created.ordenRecepcion, 7)
  } finally { stub.restore() }
})
