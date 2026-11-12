import { expect, test } from '@playwright/test'
import type { Page, Route } from '@playwright/test'
import { build } from 'vite'
import { mkdir, readFile } from 'node:fs/promises'
import { resolve } from 'node:path'

// Datos SINTÉTICOS y transporte interceptado: sin red, API ni base de datos.
// Comprueban interfaz, idempotencia y responsive de Flota; NO demuestran integración real.
const empresa = '22222222-2222-4222-8222-222222222222'
const output = resolve('test-results/app-flota')
const g = (n: number) => `77777777-7777-4777-8777-${String(n).padStart(12, '0')}`

test.beforeAll(async () => {
  test.setTimeout(120_000)
  await build({ define: { 'import.meta.env.VITE_EMPRESA_ID': JSON.stringify(empresa), 'import.meta.env.VITE_CENTRAL_ID': 'undefined' },
    build: { outDir: output, emptyOutDir: true }, logLevel: 'warn' })
  await mkdir('.impeccable/review', { recursive: true })
})

type Driver = { id: string; nombre: string; estado: string; ocupacion: string | null; turno: { id: string; vehiculoId: string; patente: string; inicio: string } | null }
type Vehicle = { id: string; patente: string; turnoId: string | null; conductorId: string | null; conductorNombre: string | null }
type Fleet = { drivers: Driver[]; vehicles: Vehicle[]; posts: { path: string; key: string | null; body: unknown }[]; failNext: boolean; role: string }

function seed(role: string): Fleet {
  const ago = (min: number) => new Date(Date.now() - min * 60_000).toISOString()
  const vehicles: Vehicle[] = [
    { id: g(101), patente: '2345-ABC', turnoId: g(201), conductorId: g(1), conductorNombre: 'María Fernanda Quispe Mamani' },
    { id: g(102), patente: '1180-KTR', turnoId: g(202), conductorId: g(2), conductorNombre: 'Jorge Luis Choque' },
    { id: g(103), patente: '3921-PLM', turnoId: null, conductorId: null, conductorNombre: null },
    { id: g(104), patente: '0457-ZXA', turnoId: null, conductorId: null, conductorNombre: null },
  ]
  const drivers: Driver[] = [
    { id: g(1), nombre: 'María Fernanda Quispe Mamani', estado: 'Disponible', ocupacion: null, turno: { id: g(201), vehiculoId: g(101), patente: '2345-ABC', inicio: ago(95) } },
    { id: g(2), nombre: 'Jorge Luis Choque', estado: 'Ocupado', ocupacion: 'Viaje', turno: { id: g(202), vehiculoId: g(102), patente: '1180-KTR', inicio: ago(240) } },
    { id: g(3), nombre: 'Ana Rocha', estado: 'FueraDeServicio', ocupacion: null, turno: null },
    { id: g(4), nombre: 'Rubén Darío Gutiérrez Villca', estado: 'FueraDeServicio', ocupacion: null, turno: null },
  ]
  return { drivers, vehicles, posts: [], failNext: false, role }
}

async function open(page: Page, path: string, role = 'Administrador'): Promise<Fleet> {
  const fleet = seed(role)
  await page.route('**/*', async (route: Route) => {
    const url = new URL(route.request().url())
    if (url.hostname !== 'radiomovil.invalid') { await route.abort('connectionrefused'); return }
    if (url.pathname.startsWith('/api/')) { await api(route, url, fleet); return }
    const asset = url.pathname.startsWith('/assets/') || url.pathname === '/radiomovil.svg' ? url.pathname : '/index.html'
    await route.fulfill({ status: 200, body: await readFile(resolve(output, `.${asset}`)), contentType:
      asset.endsWith('.js') ? 'application/javascript' : asset.endsWith('.css') ? 'text/css' : asset.endsWith('.svg') ? 'image/svg+xml' : 'text/html' })
  })
  await page.goto(`https://radiomovil.invalid${path}`)
  return fleet
}

async function api(route: Route, url: URL, fleet: Fleet) {
  const json = (body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
  const path = url.pathname.replace('/api/v1', '')
  const method = route.request().method()
  if (path === '/auth/me') { await json({ usuarioId: g(900), email: 'central@example.invalid', empresaId: empresa, roles: [fleet.role], expiraEn: new Date(Date.now() + 3_600_000).toISOString() }); return }
  if (path === '/auth/csrf') { await json({ token: 'csrf-sintetico' }); return }
  if (path === '/centrales') { await json([{ id: g(500), nombre: 'Central de prueba' }]); return }
  if (path === '/solicitudes') { await json({ page: 1, pageSize: 20, total: 0, items: [] }); return }
  if (method === 'POST') {
    const key = route.request().headers()['idempotency-key'] ?? null
    const body = route.request().postDataJSON() as Record<string, string | null>
    fleet.posts.push({ path, key, body })
    if (fleet.failNext) { fleet.failNext = false; await route.abort('connectionfailed'); return }
    if (path === '/turnos') {
      const driver = fleet.drivers.find(d => d.id === body.conductorId)!
      const vehicle = fleet.vehicles.find(v => v.id === body.vehiculoId)!
      driver.estado = 'Disponible'; driver.turno = { id: g(300), vehiculoId: vehicle.id, patente: vehicle.patente, inicio: new Date().toISOString() }
      vehicle.turnoId = g(300); vehicle.conductorId = driver.id; vehicle.conductorNombre = driver.nombre
      await json({ id: g(300), repetida: false }, 201); return
    }
    const close = /^\/turnos\/(.+)\/cerrar$/.exec(path)
    if (close) {
      const driver = fleet.drivers.find(d => d.turno?.id === close[1])!
      const vehicle = fleet.vehicles.find(v => v.turnoId === close[1])!
      driver.estado = 'FueraDeServicio'; driver.turno = null
      vehicle.turnoId = null; vehicle.conductorId = null; vehicle.conductorNombre = null
      await json({ id: close[1], repetida: false }); return
    }
    if (path === '/vehiculos') { fleet.vehicles.push({ id: g(150), patente: String(body.patente), turnoId: null, conductorId: null, conductorNombre: null }); await json({ id: g(150), repetida: false }, 201); return }
    if (path === '/conductores') { fleet.drivers.push({ id: g(50), nombre: String(body.nombre), estado: 'FueraDeServicio', ocupacion: null, turno: null }); await json({ id: g(50), repetida: false }, 201); return }
  }
  if (path === '/conductores') { await json(fleet.drivers); return }
  if (path === '/vehiculos') { await json(fleet.vehicles); return }
  if (path === '/turnos') {
    const items = [
      { id: g(201), conductorId: g(1), conductorNombre: 'María Fernanda Quispe Mamani', vehiculoId: g(101), patente: '2345-ABC', inicio: new Date(Date.now() - 95 * 60_000).toISOString(), fin: null, motivoCierre: null },
      { id: g(210), conductorId: g(3), conductorNombre: 'Ana Rocha', vehiculoId: g(103), patente: '3921-PLM', inicio: '2026-11-08T11:00:00Z', fin: '2026-11-08T19:30:00Z', motivoCierre: 'Fin de jornada' },
      { id: g(211), conductorId: g(4), conductorNombre: 'Rubén Darío Gutiérrez Villca', vehiculoId: g(104), patente: '0457-ZXA', inicio: '2026-11-08T07:00:00Z', fin: '2026-11-08T09:10:00Z', motivoCierre: null },
    ]
    await json({ page: 1, pageSize: 20, total: items.length, items }); return
  }
  await route.fulfill({ status: 404, body: '' })
}

const noOverflow = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)

test('abrir turno elige vehículo libre, envía clave y anuncia la confirmación del servidor', async ({ page }) => {
  const fleet = await open(page, '/flota')
  await expect(page.getByRole('heading', { name: /Conductores · 4/ })).toBeVisible()
  await expect(page.getByText(/«Disponible» indica turno abierto, no conexión/)).toBeVisible()
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.screenshot({ path: '.impeccable/review/flota-1440.png', fullPage: true })
  const row = page.getByRole('row').filter({ hasText: 'Ana Rocha' })
  await row.getByRole('button', { name: 'Abrir turno' }).click()
  const select = page.getByLabel('Vehículo para Ana Rocha')
  await expect(select).toBeFocused()
  await row.getByRole('button', { name: 'Abrir turno' }).click()
  await expect(page.getByText('Elige el vehículo del turno.')).toBeVisible()
  await select.selectOption({ label: '3921-PLM' })
  await page.screenshot({ path: '.impeccable/review/flota-abrir-1440.png' })
  await row.getByRole('button', { name: 'Abrir turno' }).click()
  await expect(page.getByText('Turno abierto: Ana Rocha con 3921-PLM.')).toBeVisible()
  expect(fleet.posts).toHaveLength(1)
  expect(fleet.posts[0]).toMatchObject({ path: '/turnos', body: { conductorId: g(3), vehiculoId: g(103) } })
  expect(fleet.posts[0].key).toBeTruthy()
  await expect(page.getByRole('row').filter({ hasText: 'Ana Rocha' })).toContainText('Disponible')
})

test('cierre dudoso: reintenta con la misma clave y el mismo motivo', async ({ page }) => {
  const fleet = await open(page, '/flota')
  const row = page.getByRole('row').filter({ hasText: 'María Fernanda' })
  await row.getByRole('button', { name: 'Cerrar turno' }).click()
  await page.getByLabel(/Motivo del cierre/).fill('Fin de jornada')
  fleet.failNext = true
  await row.getByRole('button', { name: 'Cerrar turno' }).click()
  await expect(page.getByText('No sabemos si se aplicó')).toBeVisible()
  await expect(page.getByLabel(/Motivo del cierre/)).not.toBeEditable()
  await expect(page.getByRole('button', { name: 'Reintentar' })).toBeFocused()
  await page.setViewportSize({ width: 390, height: 844 })
  await page.getByText('No sabemos si se aplicó').scrollIntoViewIfNeeded()
  await page.screenshot({ path: '.impeccable/review/flota-dudoso-390.png' })
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.screenshot({ path: '.impeccable/review/flota-dudoso-1440.png' })
  await page.getByRole('button', { name: 'Reintentar' }).click()
  await expect(page.getByText('Turno cerrado: María Fernanda Quispe Mamani.')).toBeVisible()
  expect(fleet.posts).toHaveLength(2)
  expect(fleet.posts[1].key).toBe(fleet.posts[0].key)
  expect(fleet.posts[1].body).toEqual({ motivo: 'Fin de jornada' })
})

test('ocupado no ofrece cerrar; la banda filtra por estado del servidor', async ({ page }) => {
  await open(page, '/flota')
  const busy = page.getByRole('row').filter({ hasText: 'Jorge Luis Choque' })
  await expect(busy).toContainText('En viaje')
  // Solo queda el acceso del administrador; ninguna acción de turno.
  await expect(busy.getByRole('button', { name: /turno/i })).toHaveCount(0)
  await page.getByRole('button', { name: /Fuera de servicio/ }).click()
  await expect(page).toHaveURL(/estado=FueraDeServicio/)
  await expect(page.getByRole('row').filter({ hasText: 'María Fernanda' })).toHaveCount(0)
  await expect(page.getByRole('row').filter({ hasText: 'Ana Rocha' })).toHaveCount(1)
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.screenshot({ path: '.impeccable/review/flota-filtro-1440.png' })
})

test('operador no ve altas; vehículos normaliza la placa antes de enviar', async ({ page }) => {
  await open(page, '/flota', 'Operador')
  await expect(page.getByRole('heading', { name: 'Registrar conductor' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Abrir turno' }).first()).toBeVisible()
  const admin = await open(page, '/flota/vehiculos')
  // Espera a que la sesión restaurada habilite el formulario: escribir antes perdía el texto (prueba intermitente).
  await expect(page.getByRole('button', { name: 'Registrar vehículo' })).toBeEnabled()
  await expect(page.getByLabel('Placa')).toBeEditable()
  await page.getByLabel('Placa').fill('ab 12_')
  await page.getByRole('button', { name: 'Registrar vehículo' }).click()
  await expect(page.getByText('La placa admite de 2 a 16 letras, números o guiones.')).toBeVisible()
  await page.getByLabel('Placa').fill(' 5521 dfg ')
  await page.getByRole('button', { name: 'Registrar vehículo' }).click()
  await expect(page.getByText('Vehículo registrado: 5521DFG.')).toBeVisible()
  expect(admin.posts.at(-1)).toMatchObject({ path: '/vehiculos', body: { patente: '5521DFG' } })
})

test('responsive de flota sin desbordes en todos los tamaños', async ({ page }) => {
  await open(page, '/flota')
  await expect(page.getByRole('heading', { name: /Conductores · 4/ })).toBeVisible()
  const sizes = [[1920, 1080], [1440, 900], [1280, 800], [1024, 768], [820, 1180], [768, 1024], [390, 844], [360, 740], [320, 568], [844, 390]] as const
  for (const [width, height] of sizes) {
    await page.setViewportSize({ width, height })
    expect(await noOverflow(page), `${width}x${height}`).toBe(true)
  }
  for (const [width, height, name] of [[1280, 800, 'flota-1280'], [820, 1180, 'flota-820'], [390, 844, 'flota-390']] as const) {
    await page.setViewportSize({ width, height })
    // Captura completa desde el inicio del documento: la topbar es sticky.
    await page.evaluate(() => window.scrollTo(0, 0))
    await page.screenshot({ path: `.impeccable/review/${name}.png`, fullPage: true })
  }
  for (const [path, name] of [['/flota/vehiculos', 'vehiculos'], ['/flota/turnos', 'turnos']] as const) {
    await page.goto(`https://radiomovil.invalid${path}`)
    await expect(page.locator('.fleet-table')).toBeVisible()
    for (const [width, height] of [[1440, 900], [390, 844]] as const) {
      await page.setViewportSize({ width, height })
      expect(await noOverflow(page), `${path} ${width}`).toBe(true)
      await page.screenshot({ path: `.impeccable/review/flota-${name}-${width}.png`, fullPage: true })
    }
  }
})
