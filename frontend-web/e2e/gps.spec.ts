import { expect, test } from '@playwright/test'
import type { BrowserContext, Page, Route } from '@playwright/test'
import { build } from 'vite'
import { mkdir, readFile } from 'node:fs/promises'
import { resolve } from 'node:path'

// SINTÉTICO: geolocalización EMULADA por Playwright y API interceptada, sin red, backend ni teléfono.
// Comprueba la interfaz de la herramienta de prueba; NO demuestra GPS real (eso se valida con el teléfono).
const empresa = '22222222-2222-4222-8222-222222222222'
const output = resolve('test-results/app-gps')
const g = (n: number) => `88888888-8888-4888-8888-${String(n).padStart(12, '0')}`
const ago = (s: number) => new Date(Date.now() - s * 1000).toISOString()

test.beforeAll(async () => {
  test.setTimeout(120_000)
  await build({ define: { 'import.meta.env.VITE_EMPRESA_ID': JSON.stringify(empresa), 'import.meta.env.VITE_CENTRAL_ID': 'undefined' },
    build: { outDir: output, emptyOutDir: true }, logLevel: 'warn' })
  await mkdir('.impeccable/review', { recursive: true })
})

type Fake = { role: string; shift: boolean; fixes: unknown[]; failNextFix: number | null }

async function open(page: Page, context: BrowserContext, path: string, role: string, shift = true): Promise<Fake> {
  const fake: Fake = { role, shift, fixes: [], failNextFix: null }
  await page.route('**/*', async (route: Route) => {
    const url = new URL(route.request().url())
    if (url.hostname !== 'radiomovil.invalid') { await route.abort('connectionrefused'); return }
    if (url.pathname.startsWith('/api/')) { await api(route, url, fake); return }
    const asset = url.pathname.startsWith('/assets/') || url.pathname === '/radiomovil.svg' ? url.pathname : '/index.html'
    await route.fulfill({ status: 200, body: await readFile(resolve(output, `.${asset}`)), contentType:
      asset.endsWith('.js') ? 'application/javascript' : asset.endsWith('.css') ? 'text/css' : asset.endsWith('.svg') ? 'image/svg+xml' : 'text/html' })
  })
  await context.grantPermissions(['geolocation'], { origin: 'https://radiomovil.invalid' })
  await context.setGeolocation({ latitude: -16.4955, longitude: -68.1336, accuracy: 9 })
  await page.goto(`https://radiomovil.invalid${path}`)
  return fake
}

async function api(route: Route, url: URL, fake: Fake) {
  const json = (body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
  const path = url.pathname.replace('/api/v1', '')
  if (path === '/auth/me') { await json({ usuarioId: g(900), email: 'jorge@example.invalid', empresaId: empresa, roles: [fake.role], expiraEn: new Date(Date.now() + 3_600_000).toISOString() }); return }
  if (path === '/auth/csrf') { await json({ token: 'csrf-sintetico' }); return }
  if (path === '/conductor/yo') { await json({ conductorId: g(1), nombre: 'Jorge Mamani', estado: fake.shift ? 'Disponible' : 'FueraDeServicio', ocupacion: null,
    turno: fake.shift ? { id: g(2), vehiculoId: g(3), patente: '2345-ABC', inicio: ago(600) } : null, estadoGps: 'SinSenal', ultimaRecepcion: null, intervaloEnvioSegundos: 5 }); return }
  if (path === '/conductor/vehiculos') { await json([{ vehiculoId: g(3), patente: '2345-ABC', libre: true }]); return }
  if (path === '/conductor/turno' && route.request().method() === 'POST') { fake.shift = true; await json({ id: g(2), repetida: false }, 201); return }
  if (path === '/conductor/ubicaciones') {
    fake.fixes.push(route.request().postDataJSON())
    if (fake.failNextFix) { const status = fake.failNextFix; fake.failNextFix = null; await route.fulfill({ status, body: 'No tienes un turno abierto.' }); return }
    await json({ aceptada: true, motivo: null, recibidaEn: new Date().toISOString(), estadoGps: 'Reciente', intervaloMinimoSegundos: 3 }); return
  }
  if (path === '/ubicaciones') {
    await json({ servidorAhora: new Date().toISOString(), umbrales: { recienteSegundos: 30, desactualizadoSegundos: 90 }, items: [
      { conductorId: g(1), nombre: 'Jorge Mamani', turnoId: g(2), patente: '2345-ABC', latitud: -16.4955, longitud: -68.1336, precisionMetros: 9, capturadaEn: ago(4), recibidaEn: ago(4), antiguedadSegundos: 4, estadoGps: 'Reciente' },
      { conductorId: g(4), nombre: 'Ana Rocha', turnoId: g(5), patente: '1180-KTR', latitud: -16.503, longitud: -68.12, precisionMetros: 25, capturadaEn: ago(55), recibidaEn: ago(55), antiguedadSegundos: 55, estadoGps: 'Desactualizado' },
      { conductorId: g(6), nombre: 'Rubén Villca', turnoId: g(7), patente: '0457-ZXA', latitud: -16.51, longitud: -68.14, precisionMetros: 12, capturadaEn: ago(400), recibidaEn: ago(400), antiguedadSegundos: 400, estadoGps: 'SinSenal' },
    ] }); return
  }
  if (path === '/conductores') { await json([{ id: g(1), nombre: 'Jorge Mamani', estado: 'FueraDeServicio', ocupacion: null, turno: null, accesoEmail: null }]); return }
  if (path === '/vehiculos') { await json([{ id: g(3), patente: '2345-ABC', turnoId: null, conductorId: null, conductorNombre: null }]); return }
  if (path === '/autorizaciones-vehiculo') { await json([]); return }
  if (path === '/centrales') { await json([{ id: g(500), nombre: 'Central de prueba' }]); return }
  if (path === '/solicitudes') { await json({ page: 1, pageSize: 20, total: 0, items: [] }); return }
  await route.fulfill({ status: 404, body: '' })
}

test('conductor: inicia turno, comparte lecturas del dispositivo y se detiene si el servidor cierra el turno', async ({ page, context }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  const fake = await open(page, context, '/conductor', 'Conductor', false)
  await expect(page.getByRole('heading', { name: 'Jorge Mamani' })).toBeVisible()
  await page.screenshot({ path: '.impeccable/review/gps-conductor-sin-turno-390.png', fullPage: true })
  await page.getByRole('button', { name: 'Iniciar turno' }).click()
  await expect(page.getByRole('button', { name: 'Compartir mi ubicación' })).toBeVisible()
  await page.getByRole('button', { name: 'Compartir mi ubicación' }).click()
  await expect(page.getByText('Compartiendo ubicación')).toBeVisible()
  await expect(page.getByText('-16.49550, -68.13360')).toBeVisible()
  await expect.poll(() => fake.fixes.length).toBeGreaterThan(0)
  expect(fake.fixes[0]).toMatchObject({ latitud: -16.4955, longitud: -68.1336, precisionMetros: 9 })
  await expect(page.getByText('Envíos aceptados')).toBeVisible()
  await page.screenshot({ path: '.impeccable/review/gps-conductor-enviando-390.png', fullPage: true })
  fake.failNextFix = 409
  await context.setGeolocation({ latitude: -16.4961, longitude: -68.1340, accuracy: 7 })
  await expect(page.getByText('Ubicación detenida')).toBeVisible({ timeout: 15_000 })
})

test('la central solo dibuja GPS reciente o desactualizado; sin señal queda en la lista', async ({ page, context }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await open(page, context, '/flota/ubicaciones', 'Operador')
  await expect(page.getByRole('heading', { name: /Conductores en turno · 3/ })).toBeVisible()
  await expect(page.getByText('Última recepción hace 6 min')).toBeVisible()
  const markers = page.locator('.map-marker')
  const drew = await page.locator('.live-map').count()
  // El lienzo debe llenar el marco: maplibre-gl.css sin capa anulaba el inset y dejaba solo una franja.
  const fills = () => page.evaluate(() => {
    const frame = document.querySelector('.map-frame')!.getBoundingClientRect()
    const canvas = document.querySelector('.maplibregl-canvas')?.getBoundingClientRect()
    return { frame: Math.round(frame.height), canvas: Math.round(canvas?.height ?? 0) }
  })
  if (drew) {
    await expect(markers).toHaveCount(2)
    await expect(page.locator('.map-sinsenal')).toHaveCount(0)
    await expect.poll(async () => { const s = await fills(); return s.frame - s.canvas }).toBeLessThanOrEqual(2)
  }
  await page.waitForTimeout(500)
  await page.screenshot({ path: '.impeccable/review/gps-ubicaciones-1440.png' })
  await page.setViewportSize({ width: 390, height: 844 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
  if (drew) await expect.poll(async () => { const s = await fills(); return s.frame - s.canvas }).toBeLessThanOrEqual(2)
  await page.screenshot({ path: '.impeccable/review/gps-ubicaciones-390.png', fullPage: true })
})

test('administrador: acceso y vehículos autorizados en la fila del conductor', async ({ page, context }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await open(page, context, '/flota', 'Administrador')
  await page.getByRole('button', { name: 'Sin acceso · configurar' }).click()
  await expect(page.getByLabel('Contraseña inicial')).toBeVisible()
  await page.getByRole('button', { name: 'Crear acceso' }).click()
  await expect(page.getByText('Escribe un correo válido.')).toBeVisible()
  await page.screenshot({ path: '.impeccable/review/gps-acceso-1440.png' })
  for (const [width, height] of [[1024, 768], [390, 844], [320, 568]] as const) {
    await page.setViewportSize({ width, height })
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), `${width}`).toBe(true)
  }
  await page.screenshot({ path: '.impeccable/review/gps-acceso-390.png', fullPage: true })
})
