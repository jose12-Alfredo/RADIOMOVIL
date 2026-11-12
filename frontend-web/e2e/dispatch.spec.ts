import { expect, test } from '@playwright/test'
import type { Page, Route } from '@playwright/test'
import { build } from 'vite'
import { mkdir, readFile } from 'node:fs/promises'
import { resolve } from 'node:path'

// SINTÉTICO: API interceptada, sin red ni backend. Verifica la interfaz de asignación directa;
// la transacción real (exclusividad, idempotencia, trigger) se prueba en el backend contra Neon Testing.
const empresa = '33333333-3333-4333-8333-333333333333'
const output = resolve('test-results/app-dispatch')
const g = (n: number) => `99999999-9999-4999-8999-${String(n).padStart(12, '0')}`
const ago = (s: number) => new Date(Date.now() - s * 1000).toISOString()
const solicitud = g(100)

test.beforeAll(async () => {
  test.setTimeout(120_000)
  await build({ define: { 'import.meta.env.VITE_EMPRESA_ID': JSON.stringify(empresa), 'import.meta.env.VITE_CENTRAL_ID': 'undefined' },
    build: { outDir: output, emptyOutDir: true }, logLevel: 'warn' })
  await mkdir('.impeccable/review', { recursive: true })
})

type Fake = { estado: number; assigns: { key: string | null; body: unknown }[]; created: unknown[]; failAssign: number[]; seen: boolean; tripEstado?: string }

const request = (estado: number) => ({ id: solicitud, empresaId: empresa, centralId: g(500), origen: 'Av. Arce y Pinilla, frente al banco', estado,
  creadaEn: ago(240), ordenRecepcion: 7, telefonoPasajero: null, nombrePasajero: 'Pasajero de prueba', destino: 'Terminal de buses', observaciones: null,
  latitud: -16.5, longitud: -68.13, destinoLatitud: -16.49, destinoLongitud: -68.15 })

async function open(page: Page, path: string, init: Partial<Fake> = {}): Promise<Fake> {
  const fake: Fake = { estado: 0, assigns: [], created: [], failAssign: [], seen: false, ...init }
  await page.route('**/*', async (route: Route) => {
    const url = new URL(route.request().url())
    if (url.hostname !== 'radiomovil.invalid') { await route.abort('connectionrefused'); return }
    if (url.pathname.startsWith('/api/')) { await api(route, url, fake); return }
    const asset = url.pathname.startsWith('/assets/') || url.pathname === '/radiomovil.svg' ? url.pathname : '/index.html'
    await route.fulfill({ status: 200, body: await readFile(resolve(output, `.${asset}`)), contentType:
      asset.endsWith('.js') ? 'application/javascript' : asset.endsWith('.css') ? 'text/css' : asset.endsWith('.svg') ? 'image/svg+xml' : 'text/html' })
  })
  await page.goto(`https://radiomovil.invalid${path}`)
  return fake
}

async function api(route: Route, url: URL, fake: Fake) {
  const json = (body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
  const path = url.pathname.replace('/api/v1', '')
  const method = route.request().method()
  if (path === '/auth/me') { await json({ usuarioId: g(900), email: 'operadora@example.invalid', empresaId: empresa, roles: ['Operador'], expiraEn: new Date(Date.now() + 3_600_000).toISOString() }); return }
  if (path === '/auth/csrf') { await json({ token: 'csrf-sintetico' }); return }
  if (path === '/centrales') { await json([{ id: g(500), nombre: 'Central de prueba' }]); return }
  if (path === '/ubicaciones') { await json({ servidorAhora: new Date().toISOString(), umbrales: { recienteSegundos: 30, desactualizadoSegundos: 90 }, items: [
    { conductorId: g(1), nombre: 'Jorge Mamani', turnoId: g(2), patente: '2345-ABC', latitud: -16.4995, longitud: -68.1335, precisionMetros: 9, capturadaEn: ago(4), recibidaEn: ago(4), antiguedadSegundos: 4, estadoGps: 'Reciente' },
  ] }); return }
  if (path === '/solicitudes' && method === 'POST') {
    const body = route.request().postDataJSON() as Record<string, unknown>
    fake.created.push(body)
    await json({ ...request(0), id: g(101), origen: body.origen, ordenRecepcion: 8, latitud: body.latitud ?? null, longitud: body.longitud ?? null,
      destinoLatitud: body.destinoLatitud ?? null, destinoLongitud: body.destinoLongitud ?? null }, 201); return
  }
  if (path === '/solicitudes') { await json({ page: 1, pageSize: 50, total: fake.estado === 0 ? 1 : 0, items: fake.estado === 0 ? [request(0)] : [] }); return }
  if (path === `/solicitudes/${solicitud}`) { await json(request(fake.estado)); return }
  if (path === `/despacho/solicitudes/${solicitud}/candidatos`) { await json({ solicitudId: solicitud, estadoSolicitud: 'Pendiente', recogidaLatitud: -16.5, recogidaLongitud: -68.13, items: [
    { conductorId: g(1), nombre: 'Jorge Mamani', patente: '2345-ABC', estadoGps: 'Reciente', antiguedadSegundos: 4, latitud: -16.4995, longitud: -68.1335, distanciaMetros: 452 },
    { conductorId: g(4), nombre: 'Ana Rocha', patente: '1180-KTR', estadoGps: 'Desactualizado', antiguedadSegundos: 55, latitud: -16.515, longitud: -68.12, distanciaMetros: 2310 },
    { conductorId: g(6), nombre: 'Rubén Villca', patente: '0457-ZXA', estadoGps: 'SinSenal', antiguedadSegundos: null, latitud: null, longitud: null, distanciaMetros: null },
  ] }); return }
  if (path === `/despacho/solicitudes/${solicitud}/asignar`) {
    fake.assigns.push({ key: route.request().headers()['idempotency-key'] ?? null, body: route.request().postDataJSON() })
    const fail = fake.failAssign.shift()
    if (fail === 409) { await route.fulfill({ status: 409, contentType: 'text/plain', body: 'El conductor ya tiene una carrera activa.' }); return }
    if (fail) { await route.fulfill({ status: fail, body: '' }); return }
    fake.estado = 2
    await json({ solicitudId: solicitud, intentoId: null, viajeId: g(300), estado: 2, fecha: new Date().toISOString(), repetida: fake.assigns.length > 1 }, fake.assigns.length > 1 ? 200 : 201); return
  }
  if (path === '/despacho/carreras') { await json(fake.estado === 2 ? [{ viajeId: g(300), solicitudId: solicitud, ordenRecepcion: 7, origen: 'Av. Arce y Pinilla, frente al banco',
    destino: 'Terminal de buses', estado: fake.tripEstado ?? 'Asignado', asignadoEn: ago(30), recibidaEn: fake.seen ? ago(12) : null, conductorId: g(1), conductorNombre: 'Jorge Mamani', patente: '2345-ABC',
    ultimoPasoEn: fake.tripEstado ? ago(5) : null }] : []); return }
  await route.fulfill({ status: 404, body: '' })
}

const noOverflow = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)

const dialog = (page: Page) => page.getByRole('dialog', { name: 'Ubicación del pasajero' })
const pasteInto = async (page: Page, text: string) => {
  await page.getByLabel(/Pegar enlace o coordenadas/).fill(text)
  await page.getByRole('button', { name: 'Ubicar', exact: true }).click()
}

test('mapa grande: enlace para recogida, coordenadas para destino, sin pisarse, y se envían exactas', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  const fake = await open(page, '/solicitudes')
  await page.getByLabel('Origen').fill('Plaza Abaroa, esquina 20 de Octubre')
  await page.getByRole('button', { name: 'Abrir mapa grande' }).click()
  await expect(dialog(page)).toBeVisible()
  await expect(dialog(page).locator('.maplibregl-canvas')).toBeVisible()
  await expect(page.getByRole('radio', { name: /Recogida/ })).toHaveAttribute('aria-checked', 'true')
  await expect(page.getByLabel(/Pegar enlace o coordenadas/)).toBeFocused()
  await pasteInto(page, 'Estoy aquí https://maps.google.com/maps?q=-16.500123%2C-68.130456&z=17')
  await expect(page.locator('.location-feedback')).toContainText('Recogida tomada del enlace: -16.500123, -68.130456')
  await page.getByRole('radio', { name: /Destino/ }).click()
  await expect(page.locator('.location-editing')).toHaveText('Tocar coloca: Destino')
  await pasteInto(page, '-16.49, -68.15')
  // Marcar el centro mientras se edita el destino no toca la recogida.
  await page.getByRole('button', { name: 'Marcar el centro' }).click()
  await expect(page.getByRole('radio', { name: /Recogida/ })).toContainText('-16.500123, -68.130456')
  await pasteInto(page, '-16.49, -68.15')
  await expect(page.getByRole('radio', { name: /Destino/ })).toContainText('-16.490000, -68.150000')
  await expect(page.locator('.map-pin')).toHaveCount(2)
  await page.waitForTimeout(500)
  await page.screenshot({ path: '.impeccable/review/ubicacion-1440.png' })
  await page.getByRole('button', { name: 'Confirmar ubicación' }).click()
  await expect(dialog(page)).toBeHidden()
  await expect(page.getByRole('button', { name: 'Abrir mapa grande' })).toBeFocused()
  const summary = page.locator('.map-points-state')
  await expect(summary.nth(0)).toHaveText('-16.500123, -68.130456')
  await expect(summary.nth(1)).toHaveText('-16.490000, -68.150000')
  await page.screenshot({ path: '.impeccable/review/ubicacion-registro-1440.png' })

  // Reabrir conserva lo confirmado; Escape con cambios pide confirmación y descartar no borra lo anterior.
  await page.getByRole('button', { name: 'Abrir mapa grande' }).click()
  await expect(page.getByRole('radio', { name: /Recogida/ })).toContainText('-16.500123, -68.130456')
  await page.getByRole('radio', { name: /Destino/ }).click()
  await pasteInto(page, '-16.47, -68.11')
  await page.keyboard.press('Escape')
  await expect(page.getByText('¿Salir sin guardar los cambios del mapa?')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Seguir editando' })).toBeFocused()
  await page.getByRole('button', { name: 'Descartar cambios' }).click()
  await expect(dialog(page)).toBeHidden()
  await expect(summary.nth(1)).toHaveText('-16.490000, -68.150000')

  await page.getByRole('button', { name: 'Registrar solicitud' }).click()
  await expect(page.getByText('Registrada · Orden 8')).toBeVisible()
  const sent = fake.created[0] as Record<string, number>
  expect([sent.latitud, sent.longitud, sent.destinoLatitud, sent.destinoLongitud]).toEqual([-16.500123, -68.130456, -16.49, -68.15])
  // Tras registrar, los puntos se limpian para la siguiente llamada.
  await expect(summary.nth(0)).toHaveText('Sin marcar')
})

test('mapa grande: tocar coloca solo el punto activo y el destino sigue siendo opcional', async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 768 })
  const fake = await open(page, '/solicitudes')
  await page.getByLabel('Origen').fill('Calle Jaén 711')
  await page.getByRole('button', { name: 'Abrir mapa grande' }).click()
  await pasteInto(page, '-16.495, -68.135')
  const map = dialog(page).locator('.maplibregl-canvas')
  const box = (await map.boundingBox())!
  await page.mouse.click(box.x + box.width * 0.7, box.y + box.height * 0.4)
  const moved = await page.getByRole('radio', { name: /Recogida/ }).textContent()
  expect(moved).not.toContain('-16.495000, -68.135000')
  await expect(page.getByRole('radio', { name: /Destino/ })).toContainText('Sin marcar')
  await pasteInto(page, 'https://maps.app.goo.gl/AbCdEf123')
  await expect(page.locator('.location-feedback-error')).toContainText('abreviados')
  await pasteInto(page, 'https://www.google.com/maps/place/Plaza+Murillo')
  await expect(page.locator('.location-feedback-error')).toContainText('no incluye coordenadas')
  await page.getByRole('button', { name: 'Confirmar ubicación' }).click()
  await page.getByRole('button', { name: 'Registrar solicitud' }).click()
  await expect(page.getByText('Registrada · Orden 8')).toBeVisible()
  const sent = fake.created[0] as Record<string, number | null>
  expect(Number.isFinite(sent.latitud)).toBe(true)
  expect(sent.destinoLatitud).toBeNull()
})

test('mapa grande adaptable: 320 a 1920 px y giro del teléfono, sin cortes ni controles pequeños', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await open(page, '/solicitudes')
  await page.getByLabel('Origen').fill('Av. 6 de Agosto 2170, edificio con portón verde')
  const sizes: [number, number][] = [[320, 640], [375, 667], [390, 844], [768, 1024], [1024, 768], [1366, 768], [1920, 1080], [844, 390], [667, 375]]
  for (const [width, height] of sizes) {
    await page.setViewportSize({ width, height })
    if (!(await dialog(page).isVisible())) await page.getByRole('button', { name: 'Abrir mapa grande' }).click()
    await expect(dialog(page).locator('.maplibregl-canvas')).toBeVisible()
    if (width === 1024) await pasteInto(page, '-16.5, -68.13')
    await page.waitForTimeout(350)
    const report = await page.evaluate(() => {
      const d = document.querySelector('.location-dialog')!.getBoundingClientRect()
      const map = document.querySelector('.location-map')!.getBoundingClientRect()
      const canvas = document.querySelector('.location-map .maplibregl-canvas')!.getBoundingClientRect()
      const inside = (r: DOMRect) => r.top >= -1 && r.left >= -1 && r.bottom <= innerHeight + 1 && r.right <= innerWidth + 1
      const controls = [...document.querySelectorAll<HTMLElement>('.location-dialog button, .location-dialog input')].filter(e => e.offsetParent)
      const small = controls.filter(e => { const r = e.getBoundingClientRect(); return r.height < 44 || (r.width < 44 && !e.classList.contains('text-action')) })
        .map(e => `${e.textContent || e.getAttribute('aria-label')} ${Math.round(e.getBoundingClientRect().width)}x${Math.round(e.getBoundingClientRect().height)}`)
      const actions = [...document.querySelectorAll<HTMLElement>('.location-foot button')]
      const clipped = [...document.querySelectorAll<HTMLElement>('.location-dialog *')]
        .filter(e => e.clientWidth > 0 && e.scrollWidth > e.clientWidth + 1 && getComputedStyle(e).overflowX === 'visible' && !e.closest('.maplibregl-map'))
        .map(e => String(e.className))
      return { dialogShare: d.width * d.height / (innerWidth * innerHeight), mapShare: map.width * map.height / (innerWidth * innerHeight),
        canvasMatches: Math.abs(canvas.width - map.width) < 2 && Math.abs(canvas.height - map.height) < 2,
        actionsVisible: actions.length === 2 && actions.every(a => inside(a.getBoundingClientRect())), dialogInside: inside(d), small, clipped,
        pageOverflow: document.documentElement.scrollWidth > innerWidth }
    })
    const label = `${width}x${height}`
    expect(report.dialogInside, label).toBe(true)
    expect(report.actionsVisible, label).toBe(true)
    expect(report.canvasMatches, label).toBe(true)
    expect(report.small, label).toEqual([])
    expect(report.clipped, label).toEqual([])
    expect(report.pageOverflow, label).toBe(false)
    if (width >= 1024 && height >= 700) expect(report.dialogShare, label).toBeGreaterThan(0.7)
    if (width < 768 || height < 528) expect(report.dialogShare, label).toBeGreaterThan(0.99)
    expect(report.mapShare, label).toBeGreaterThan(width < 768 ? 0.28 : 0.45)
    await page.screenshot({ path: `.impeccable/review/ubicacion-${label}.png` })
  }
  // El giro mantiene lo marcado.
  await expect(page.getByRole('radio', { name: /Recogida/ })).toContainText('-16.500000, -68.130000')
})

test('detalle pendiente: cercanos primero, asignación confirmada por el servidor y orden aún no vista', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  const fake = await open(page, `/solicitudes/${solicitud}`)
  await expect(page.getByRole('heading', { name: /Asignar carrera · 3 disponibles/ })).toBeVisible()
  const options = page.locator('.assign-option')
  await expect(options).toHaveCount(3)
  await expect(options.nth(0)).toContainText('Jorge Mamani')
  await expect(options.nth(0)).toContainText('A 450 m en línea recta')
  await expect(options.nth(1)).toContainText('A 2,3 km en línea recta')
  await expect(options.nth(2)).toContainText('Sin GPS reciente: distancia desconocida')
  const assign = page.locator('.assign-button')
  await expect(assign).toBeDisabled()
  await expect(assign).toHaveText('Elige un conductor para asignar')
  // Sin señal no se dibuja; los otros dos sí, junto con recogida y destino.
  await expect(page.locator('.assign-map .map-marker')).toHaveCount(4)
  await page.getByRole('radio', { name: /Ana Rocha/ }).check()
  await expect(page.getByText('Su última posición no es reciente.', { exact: false })).toBeVisible()
  await page.getByRole('radio', { name: /Jorge Mamani/ }).check()
  await expect(assign).toHaveText('Asignar carrera a Jorge Mamani · 2345-ABC')
  await page.waitForTimeout(400)
  await page.screenshot({ path: '.impeccable/review/asignar-detalle-1440.png', fullPage: true })
  await assign.click()
  await expect(page.getByText('Carrera asignada a Jorge Mamani · 2345-ABC')).toBeVisible()
  expect(fake.assigns).toHaveLength(1)
  expect(fake.assigns[0].body).toEqual({ conductorId: g(1) })
  expect(fake.assigns[0].key).toBeTruthy()
  // La solicitud pasa a Asignada (dato del servidor) y muestra la carrera; la orden aún no fue vista.
  await expect(page.getByText('Aún no confirmada por la app del conductor')).toBeVisible()
  await expect(page.locator('.detail-head')).toContainText('Asignada')
  await page.setViewportSize({ width: 390, height: 844 })
  expect(await noOverflow(page)).toBe(true)
  await page.screenshot({ path: '.impeccable/review/asignar-confirmada-390.png', fullPage: true })
})

test('resultado dudoso conserva la clave; un 409 muestra el motivo del servidor', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 860 })
  const fake = await open(page, `/solicitudes/${solicitud}`, { failAssign: [502, 409] })
  await page.getByRole('radio', { name: /Jorge Mamani/ }).check()
  await page.locator('.assign-button').click()
  await expect(page.getByText('No sabemos si se aplicó')).toBeVisible()
  await expect(page.getByRole('radio', { name: /Ana Rocha/ })).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Reintentar' })).toBeFocused()
  await page.screenshot({ path: '.impeccable/review/asignar-dudoso-1280.png' })
  await page.getByRole('button', { name: 'Reintentar' }).click()
  await expect(page.getByText('El conductor ya tiene una carrera activa.')).toBeVisible()
  expect(fake.assigns).toHaveLength(2)
  expect(fake.assigns[1].key).toBe(fake.assigns[0].key)
  await expect(page.getByText('Carrera asignada', { exact: false })).toHaveCount(0)
})

test('despacho: por asignar y carreras activas con la entrega a la app', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await open(page, '/despacho', { estado: 0 })
  await expect(page.getByRole('heading', { name: /Por asignar · 1/ })).toBeVisible()
  await expect(page.getByRole('link', { name: /Av. Arce y Pinilla.*Asignar/ })).toBeVisible()
  await expect(page.getByRole('heading', { name: /Carreras activas · 0/ })).toBeVisible()
  await page.unrouteAll({ behavior: 'ignoreErrors' })
  await open(page, '/despacho', { estado: 2, seen: true })
  await expect(page.getByRole('heading', { name: /Carreras activas · 1/ })).toBeVisible()
  const row = page.locator('.dispatch-row').filter({ hasText: 'Jorge Mamani' })
  await expect(row).toContainText('2345-ABC')
  await expect(row).toContainText('Asignada')
  await expect(row).toContainText('Vista')
  await page.screenshot({ path: '.impeccable/review/despacho-1440.png', fullPage: true })
  await page.setViewportSize({ width: 390, height: 844 })
  expect(await noOverflow(page)).toBe(true)
  await page.screenshot({ path: '.impeccable/review/despacho-390.png', fullPage: true })
})

test('despacho: la central ve el paso que marcó el conductor', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await open(page, '/despacho', { estado: 2, seen: true, tripEstado: 'EnPuerta' })
  const row = page.locator('.dispatch-row').filter({ hasText: 'Jorge Mamani' })
  await expect(row.locator('.state-badge')).toHaveText('En la puerta')
  await expect(row).toContainText(/Desde las \d{2}:\d{2}/)
  expect(await noOverflow(page)).toBe(true)
  await page.screenshot({ path: '.impeccable/review/despacho-en-puerta-390.png', fullPage: true })
})

