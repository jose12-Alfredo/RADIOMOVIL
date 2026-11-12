import assert from 'node:assert/strict'
import test from 'node:test'
import { formatCoords, parseLocation } from '../src/features/requests/location.ts'

function ok(text: string, lat: number, lng: number, source: 'coordenadas' | 'enlace') {
  const parsed = parseLocation(text)
  assert.ok(parsed.ok, `${text}: ${parsed.ok ? '' : parsed.error}`)
  assert.equal(parsed.lat, lat); assert.equal(parsed.lng, lng); assert.equal(parsed.source, source)
}
function bad(text: string, includes: string) {
  const parsed = parseLocation(text)
  assert.equal(parsed.ok, false, text)
  if (!parsed.ok) assert.ok(parsed.error.includes(includes), `${text} → ${parsed.error}`)
}

test('coordenadas escritas: coma, espacio, punto y coma y signos', () => {
  ok('-12.04637, -77.04279', -12.04637, -77.04279, 'coordenadas')
  ok('  -12.04637 -77.04279 ', -12.04637, -77.04279, 'coordenadas')
  ok('-12.04637;-77.04279', -12.04637, -77.04279, 'coordenadas')
  ok('40.4168,-3.7038', 40.4168, -3.7038, 'coordenadas')
  ok('+4.6, 74', 4.6, 74, 'coordenadas')
})

test('grados, minutos y segundos como los muestra Google Maps', () => {
  ok(`12°02'46.9"S 77°02'34.0"W`, -12.046361, -77.042778, 'coordenadas')
  ok('40°25′00.5″N 3°42′13.7″O', 40.416806, -3.703806, 'coordenadas')
  bad(`12°02'46.9"S 77°02'34.0"S`, 'latitud (N/S)')
})

test('rangos geográficos y 0,0', () => {
  bad('91, 10', 'latitud debe estar entre -90 y 90')
  bad('-77.04279, -190', 'longitud debe estar entre -180 y 180')
  bad('0, 0', 'no trae coordenadas reales')
  bad('', 'Pega un enlace')
  bad('Av. Arequipa 1234', 'No reconocemos el formato')
})

test('enlaces de Google Maps con coordenadas explícitas', () => {
  ok('https://maps.google.com/?q=-12.04637,-77.04279', -12.04637, -77.04279, 'enlace')
  ok('https://www.google.com/maps/search/?api=1&query=-12.04637%2C-77.04279', -12.04637, -77.04279, 'enlace')
  ok('https://www.google.com/maps/@-12.0463,-77.0427,17z', -12.0463, -77.0427, 'enlace')
  ok('https://www.google.com/maps/search/-12.04637,+-77.04279?entry=tts', -12.04637, -77.04279, 'enlace')
  ok('https://www.google.com.pe/maps/place/-12.04637,-77.04279', -12.04637, -77.04279, 'enlace')
  ok('https://www.google.com/maps/dir/?api=1&destination=-12.1,-77.0', -12.1, -77, 'enlace')
  ok('https://maps.google.com/maps?ll=-12.04,-77.03&z=15', -12.04, -77.03, 'enlace')
  ok('maps.google.com/?q=-12.04637,-77.04279', -12.04637, -77.04279, 'enlace')
  ok('geo:-12.04637,-77.04279?z=17', -12.04637, -77.04279, 'enlace')
})

test('el lugar marcado (!3d!4d) tiene prioridad sobre el centro de la vista (@)', () => {
  ok('https://www.google.com/maps/place/Plaza+Mayor/@-12.0450,-77.0300,17z/data=!3m1!4b1!4m6!3m5!1s0x0:0x0!8m2!3d-12.0464!4d-77.0428',
    -12.0464, -77.0428, 'enlace')
})

test('mensaje de WhatsApp con el enlace dentro', () => {
  ok('Estoy aquí 👉 https://maps.google.com/maps?q=-12.04637%2C-77.04279&z=17&hl=es gracias', -12.04637, -77.04279, 'enlace')
})

test('no inventa coordenadas: enlaces sin coordenadas, abreviados u otros sitios', () => {
  bad('https://www.google.com/maps/place/Plaza+Mayor+de+Lima', 'no incluye coordenadas')
  bad('https://www.google.com/maps/search/?api=1&query=Miraflores', 'no incluye coordenadas')
  bad('https://maps.app.goo.gl/AbCdEf123', 'abreviados')
  bad('https://goo.gl/maps/AbCdEf123', 'abreviados')
  bad('https://example.com/maps/@-12.04,-77.03,17z', 'Solo se interpretan enlaces de Google Maps')
  bad('https://google.com.evil.example/maps/@-12.04,-77.03,17z', 'Solo se interpretan')
  bad('http://127.0.0.1/maps?q=-12.04,-77.03', 'Solo se interpretan')
  bad('https://www.google.com/maps/@91,-77.03,17z', 'latitud')
  bad('https://www.google.com/maps/place/%E0%A4%A', 'mal copiado')
})

test('formato de lectura con 6 decimales', () => {
  assert.equal(formatCoords(-12.04637, -77.04279), '-12.046370, -77.042790')
})
