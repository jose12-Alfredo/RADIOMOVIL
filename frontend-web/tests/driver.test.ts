import assert from 'node:assert/strict'
import test from 'node:test'
import { HttpError, NetworkError } from '../src/services/http.ts'
import { classifySendError, decodeMe, decodeMyVehicles, decodeReceipt, fixFromPosition } from '../src/features/driver/driver.ts'
import { decodeAuthorizations, decodeLocations, formatAge, validateAccessPassword } from '../src/features/fleet/fleet.ts'

const id = '11111111-1111-4111-8111-111111111111'
const now = '2026-11-09T18:00:00Z'

test('decodifica al conductor propio y rechaza estados inventados', () => {
  const me = decodeMe({ conductorId: id, nombre: 'Jorge', estado: 'Disponible', ocupacion: null, turno: { id, vehiculoId: id, patente: 'ABC123', inicio: now },
    estadoGps: 'Reciente', ultimaRecepcion: now, intervaloEnvioSegundos: 5 })
  assert.equal(me.turno?.patente, 'ABC123')
  assert.throws(() => decodeMe({ ...me, estadoGps: 'Conectado' }))
  assert.throws(() => decodeMe({ ...me, intervaloEnvioSegundos: 0 }))
  assert.deepEqual(decodeMyVehicles([{ vehiculoId: id, patente: 'ABC123', libre: true }])[0].libre, true)
  assert.equal(decodeReceipt({ aceptada: false, motivo: 'Repetida', recibidaEn: now, estadoGps: 'Reciente', intervaloMinimoSegundos: 3 }).motivo, 'Repetida')
})

test('solo convierte lecturas reales del dispositivo; descarta valores no finitos', () => {
  const fix = fixFromPosition({ coords: { latitude: -16.5, longitude: -68.15, accuracy: 7.4 }, timestamp: Date.parse(now) })
  assert.deepEqual(fix, { latitud: -16.5, longitud: -68.15, precisionMetros: 7.4, capturadaEn: '2026-11-09T18:00:00.000Z' })
  assert.equal(fixFromPosition({ coords: { latitude: Number.NaN, longitude: 0, accuracy: 5 }, timestamp: 1 }), null)
  assert.equal(fixFromPosition({ coords: { latitude: 1, longitude: 1, accuracy: 0 }, timestamp: 1 }), null)
})

test('clasifica respuestas del envío: sin turno detiene, 429 espera, red reintenta', () => {
  assert.equal(classifySendError(new HttpError(409, 'No tienes un turno abierto.')).kind, 'no-shift')
  assert.equal(classifySendError(new HttpError(429, 'x')).kind, 'wait')
  assert.equal(classifySendError(new HttpError(401, undefined)).kind, 'session')
  assert.equal(classifySendError(new NetworkError(new Error('x'))).kind, 'offline')
  assert.equal(classifySendError(new HttpError(400, 'Coordenadas fuera de rango.')).kind, 'rejected')
})

test('ubicaciones: sin señal puede no tener punto; reciente sin punto es contrato inválido', () => {
  const base = { conductorId: id, nombre: 'Jorge', turnoId: id, patente: 'ABC123' }
  const snapshot = decodeLocations({ servidorAhora: now, umbrales: { recienteSegundos: 30, desactualizadoSegundos: 90 }, items: [
    { ...base, latitud: -16.5, longitud: -68.15, precisionMetros: 6, capturadaEn: now, recibidaEn: now, antiguedadSegundos: 4, estadoGps: 'Reciente' },
    { ...base, latitud: null, longitud: null, precisionMetros: null, capturadaEn: null, recibidaEn: null, antiguedadSegundos: null, estadoGps: 'SinSenal' },
  ] })
  assert.equal(snapshot.items[1].latitud, null)
  assert.throws(() => decodeLocations({ servidorAhora: now, umbrales: { recienteSegundos: 30, desactualizadoSegundos: 90 },
    items: [{ ...base, latitud: null, longitud: null, recibidaEn: null, estadoGps: 'Reciente' }] }))
  assert.throws(() => decodeLocations({ servidorAhora: now, umbrales: { recienteSegundos: 30, desactualizadoSegundos: 90 },
    items: [{ ...base, latitud: 120, longitud: 0, recibidaEn: now, antiguedadSegundos: 1, estadoGps: 'Reciente' }] }))
  assert.equal(formatAge(12), 'hace 12 s')
  assert.equal(formatAge(185), 'hace 3 min')
})

test('autorizaciones y contraseña inicial con las reglas del backend', () => {
  assert.equal(decodeAuthorizations([{ id, conductorId: id, conductorNombre: 'J', vehiculoId: id, patente: 'A1', vehiculoLibre: true, autorizadaEn: now }]).length, 1)
  assert.throws(() => decodeAuthorizations([{ id }]))
  assert.ok(validateAccessPassword('corta'))
  assert.ok(validateAccessPassword('sinsimbolosNi9'))
  assert.equal(validateAccessPassword('Conductor-2026!'), null)
})
