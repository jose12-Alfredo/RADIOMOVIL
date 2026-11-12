import assert from 'node:assert/strict'
import test from 'node:test'
import { decodeActiveTrips, decodeAssignment, decodeNearby, formatDistance, tripProgress } from '../src/features/dispatch/dispatch.ts'

const g = (n: number) => `99999999-9999-4999-8999-${String(n).padStart(12, '0')}`
const nearby = (items: unknown[]) => ({ solicitudId: g(1), estadoSolicitud: 'Pendiente', recogidaLatitud: -16.5, recogidaLongitud: -68.13, items })
const candidate = { conductorId: g(2), nombre: 'Jorge', patente: '2345-ABC', estadoGps: 'Reciente', antiguedadSegundos: 4, latitud: -16.4995, longitud: -68.1335, distanciaMetros: 452 }

test('candidatos cercanos: decodifica el contrato y conserva el orden del servidor', () => {
  const result = decodeNearby(nearby([candidate, { ...candidate, conductorId: g(3), estadoGps: 'SinSenal', antiguedadSegundos: null, latitud: null, longitud: null, distanciaMetros: null }]))
  assert.equal(result.items.length, 2)
  assert.equal(result.items[0].distanciaMetros, 452)
  assert.equal(result.items[1].estadoGps, 'SinSenal')
  assert.equal(decodeNearby({ ...nearby([]), recogidaLatitud: null, recogidaLongitud: null }).recogidaLatitud, null)
})

test('candidatos: «Sin señal» con posición o distancia se rechaza (GPS viejo nunca como actual)', () => {
  assert.throws(() => decodeNearby(nearby([{ ...candidate, estadoGps: 'SinSenal' }])))
  assert.throws(() => decodeNearby(nearby([{ ...candidate, estadoGps: 'Otro' }])))
  assert.throws(() => decodeNearby(nearby([{ ...candidate, distanciaMetros: 'cerca' }])))
})

test('carreras activas y respuesta de asignación', () => {
  const trip = { viajeId: g(4), solicitudId: g(1), ordenRecepcion: 7, origen: 'Plaza', destino: null, estado: 'Asignado', asignadoEn: new Date().toISOString(),
    recibidaEn: null, conductorId: g(2), conductorNombre: 'Jorge', patente: '2345-ABC' }
  assert.equal(decodeActiveTrips([trip])[0].recibidaEn, null)
  assert.throws(() => decodeActiveTrips([{ ...trip, estado: 'Aceptado' }]))
  assert.deepEqual(decodeAssignment({ solicitudId: g(1), intentoId: null, viajeId: g(4), estado: 2, fecha: new Date().toISOString(), repetida: false }), { id: g(4), repetida: false })
  assert.throws(() => decodeAssignment({ viajeId: null, repetida: false }))
})

test('pasos del conductor: estados nuevos y línea de progreso', () => {
  const base = { viajeId: g(4), solicitudId: g(1), ordenRecepcion: 7, origen: 'Plaza', destino: null, asignadoEn: '2026-11-10T20:00:00Z',
    recibidaEn: '2026-11-10T20:00:10Z', conductorId: g(2), conductorNombre: 'Jorge', patente: '2345-ABC' }
  const time = (iso: string) => iso.slice(11, 16)
  for (const estado of ['EnCamino', 'EnPuerta', 'EnCurso']) assert.equal(decodeActiveTrips([{ ...base, estado, ultimoPasoEn: '2026-11-10T20:05:00Z' }])[0].estado, estado)
  const [enPuerta] = decodeActiveTrips([{ ...base, estado: 'EnPuerta', ultimoPasoEn: '2026-11-10T20:05:00Z' }])
  assert.deepEqual(tripProgress(enPuerta, time), { text: 'Desde las 20:05', unseen: false })
  const [asignada] = decodeActiveTrips([{ ...base, estado: 'Asignado', recibidaEn: null }])
  assert.equal(asignada.ultimoPasoEn, null)
  assert.deepEqual(tripProgress(asignada, time), { text: 'Sin confirmar en la app', unseen: true })
  assert.throws(() => decodeActiveTrips([{ ...base, estado: 'Finalizado' }]))
  assert.throws(() => decodeActiveTrips([{ ...base, estado: 'EnCurso', ultimoPasoEn: 'luego' }]))
})

test('distancia legible en línea recta', () => {
  assert.equal(formatDistance(452), '450 m')
  assert.equal(formatDistance(3), '10 m')
  assert.equal(formatDistance(2310), '2,3 km')
})
