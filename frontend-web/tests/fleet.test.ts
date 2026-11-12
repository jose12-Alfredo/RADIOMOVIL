import assert from 'node:assert/strict'
import test from 'node:test'
import { HttpError, NetworkError } from '../src/services/http.ts'
import { classifyActionError, decodeDrivers, decodeOperation, decodeShiftPage, decodeVehicles, normalizePlate, validateDriverName, validatePlate } from '../src/features/fleet/fleet.ts'

const id = '11111111-1111-4111-8111-111111111111'
const other = '22222222-2222-4222-8222-222222222222'
const shift = { id: other, vehiculoId: id, patente: 'ABC123', inicio: '2026-11-09T12:00:00Z' }

test('decodifica estados de conductor calculados por el servidor', () => {
  const [free, busy, off] = decodeDrivers([
    { id, nombre: 'Ana', estado: 'Disponible', ocupacion: null, turno: shift },
    { id, nombre: 'Luis', estado: 'Ocupado', ocupacion: 'Viaje', turno: shift },
    { id, nombre: 'Rosa', estado: 'FueraDeServicio', ocupacion: null, turno: null },
  ])
  assert.equal(free.turno?.patente, 'ABC123')
  assert.equal(busy.ocupacion, 'Viaje')
  assert.equal(off.turno, null)
})

test('rechaza combinaciones de estado incoherentes en lugar de mostrarlas', () => {
  assert.throws(() => decodeDrivers([{ id, nombre: 'A', estado: 'Conectado', ocupacion: null, turno: null }]))
  assert.throws(() => decodeDrivers([{ id, nombre: 'A', estado: 'Disponible', ocupacion: null, turno: null }]))
  assert.throws(() => decodeDrivers([{ id, nombre: 'A', estado: 'Ocupado', ocupacion: null, turno: shift }]))
  assert.throws(() => decodeDrivers([{ id, nombre: 'A', estado: 'Disponible', ocupacion: 'Viaje', turno: shift }]))
  assert.throws(() => decodeDrivers([{ id, nombre: 'A', estado: 'Disponible', ocupacion: null, turno: { ...shift, inicio: 'ayer' } }]))
})

test('decodifica vehículos, historial y resultado de operación', () => {
  const [inShift, free] = decodeVehicles([
    { id, patente: 'ABC123', turnoId: other, conductorId: other, conductorNombre: 'Ana' },
    { id: other, patente: 'XYZ9', turnoId: null, conductorId: null, conductorNombre: null },
  ])
  assert.equal(inShift.conductorNombre, 'Ana')
  assert.equal(free.turnoId, null)
  assert.throws(() => decodeVehicles([{ id, patente: 'A', turnoId: other, conductorId: null, conductorNombre: null }]))
  const page = decodeShiftPage({ page: 1, pageSize: 20, total: 1, items: [{ id, conductorId: other, conductorNombre: 'Ana', vehiculoId: id, patente: 'ABC123', inicio: shift.inicio, fin: null, motivoCierre: null }] })
  assert.equal(page.items[0].fin, null)
  assert.throws(() => decodeShiftPage({ page: 1, pageSize: 20, items: [] }))
  assert.deepEqual(decodeOperation({ id, repetida: true }), { id, repetida: true })
  assert.throws(() => decodeOperation({ id }))
})

test('valida placa y nombre como el backend, sin sustituirlo', () => {
  assert.equal(normalizePlate(' abc 123 '), 'ABC123')
  assert.equal(validatePlate('2345-abc'), null)
  assert.ok(validatePlate(''))
  assert.ok(validatePlate('A'))
  assert.ok(validatePlate('AB_12'))
  assert.equal(validateDriverName(' Ana '), null)
  assert.ok(validateDriverName('  '))
  assert.ok(validateDriverName('x'.repeat(161)))
})

test('resultado dudoso conserva la clave; rechazo explica el motivo del servidor', () => {
  assert.equal(classifyActionError(new NetworkError(new Error('x'))).kind, 'uncertain')
  assert.equal(classifyActionError(new HttpError(500, undefined)).kind, 'uncertain')
  assert.equal(classifyActionError(new Error('json')).kind, 'uncertain')
  assert.equal(classifyActionError(new HttpError(401, undefined)).kind, 'session')
  const conflict = classifyActionError(new HttpError(409, 'El conductor ya tiene un turno abierto.'))
  assert.deepEqual(conflict, { kind: 'rejected', message: 'El conductor ya tiene un turno abierto.' })
  assert.equal(classifyActionError(new HttpError(403, undefined)).kind, 'rejected')
})
