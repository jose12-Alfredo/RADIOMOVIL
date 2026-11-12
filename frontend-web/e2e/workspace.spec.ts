import { expect, test } from '@playwright/test'
import type { Page, Route } from '@playwright/test'
import { build } from 'vite'
import { mkdir, readFile } from 'node:fs/promises'
import { resolve } from 'node:path'

// Datos sintéticos y transporte interceptado: no hay red, API ni base de datos.
// Comprueban la interfaz y el manejo de idempotencia; NO demuestran integración real.
const empresa = '22222222-2222-4222-8222-222222222222'
const central = '33333333-3333-4333-8333-333333333333'
const withCentral = resolve('test-results/app-central')
const withoutCentral = resolve('test-results/app-sin-central')

test.beforeAll(async () => {
  test.setTimeout(120_000)
  const env = (centralId: string | undefined) => ({
    'import.meta.env.VITE_EMPRESA_ID': JSON.stringify(empresa),
    'import.meta.env.VITE_CENTRAL_ID': centralId ? JSON.stringify(centralId) : 'undefined',
  })
  await build({ define: env(central), build: { outDir: withCentral, emptyOutDir: true }, logLevel: 'warn' })
  await build({ define: env(undefined), build: { outDir: withoutCentral, emptyOutDir: true }, logLevel: 'warn' })
  await mkdir('.impeccable/review', { recursive: true })
})

const session = { usuarioId: '11111111-1111-4111-8111-111111111111', email: 'operador@example.invalid', empresaId: empresa, roles: ['Operador'], expiraEn: new Date(Date.now() + 3_600_000).toISOString() }
const states = ['Pendiente', 'Ofertada', 'Asignada', 'EnViaje', 'Finalizada', 'Cancelada']
const places = ['Av. Principal y calle 5', 'Terminal de buses, puerta 2', 'Hospital central, emergencias', 'Mercado norte, entrada este', 'Plaza mayor, frente al banco, junto a la farmacia de turno y la parada de buses de la línea 12', 'Barrio San José, casa 14']

type Row = { id: string; empresaId: string; centralId: string; origen: string; estado: number; creadaEn: string; ordenRecepcion: number }
function seed(count: number): Row[] {
  return Array.from({ length: count }, (_, index) => ({
    id: `44444444-4444-4444-8444-${String(index + 1).padStart(12, '0')}`, empresaId: empresa, centralId: central,
    origen: places[index % places.length], estado: index < 23 ? 0 : (index % 5) + 1,
    creadaEn: new Date(Date.now() - (count - index) * 240_000).toISOString(), ordenRecepcion: index + 1,
  }))
}

type Backend = { rows: Row[]; posts: { key: string | null; body: unknown }[]; failNextPost: boolean; meStatus: number; listStatus: number; holdList?: Promise<void> }

async function open(page: Page, path: string, options: { rows?: number; output?: string } = {}): Promise<Backend> {
  const backend: Backend = { rows: seed(options.rows ?? 30), posts: [], failNextPost: false, meStatus: 200, listStatus: 200 }
  const output = options.output ?? withCentral
  await page.route('**/*', async (route: Route) => {
    const url = new URL(route.request().url())
    if (url.hostname !== 'radiomovil.invalid') { await route.abort('connectionrefused'); return }
    if (url.pathname.startsWith('/api/')) { await api(route, url, backend, output === withoutCentral); return }
    const asset = url.pathname.startsWith('/assets/') || url.pathname === '/radiomovil.svg' ? url.pathname : '/index.html'
    const file = resolve(output, `.${asset}`)
    await route.fulfill({ status: 200, body: await readFile(file), contentType:
      asset.endsWith('.js') ? 'application/javascript' : asset.endsWith('.css') ? 'text/css' : asset.endsWith('.svg') ? 'image/svg+xml' : 'text/html' })
  })
  await page.goto(`https://radiomovil.invalid${path}`)
  return backend
}

async function api(route: Route, url: URL, backend: Backend, noCentral: boolean) {
  const json = (body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
  const path = url.pathname.replace('/api/v1', '')
  if (path === '/auth/me') { if (backend.meStatus === 200) await json(session); else await route.fulfill({ status: backend.meStatus, body: '' }); return }
  if (path === '/auth/csrf') { await json({ token: 'csrf-sintetico' }); return }
  if (path === '/centrales') { await json(noCentral ? [] : [{ id: central, nombre: 'Central de prueba' }]); return }
  if (path === '/despacho/candidatos') { await json([{ conductorId: '55555555-5555-4555-8555-555555555555', nombre: 'Conductor sintético', ubicacionVerificada: false }]); return }
  // Asignación directa: candidatos cercanos de cada solicitud y carreras activas (contratos 2026-11-10).
  const nearby = /^\/despacho\/solicitudes\/(.+)\/candidatos$/.exec(path)
  if (nearby) { await json({ solicitudId: nearby[1], estadoSolicitud: 'Pendiente', recogidaLatitud: null, recogidaLongitud: null, items: [
    { conductorId: '55555555-5555-4555-8555-555555555555', nombre: 'Conductor sintético', patente: '1234-SIN', estadoGps: 'SinSenal', antiguedadSegundos: null, latitud: null, longitud: null, distanciaMetros: null }] }); return }
  if (path === '/despacho/carreras') { await json([]); return }
  if (path === '/solicitudes' && route.request().method() === 'POST') {
    const key = route.request().headers()['idempotency-key'] ?? null
    const body = route.request().postDataJSON() as { centralId: string; origen: string }
    backend.posts.push({ key, body })
    if (backend.failNextPost) { backend.failNextPost = false; await route.abort('connectionfailed'); return }
    const created: Row = { id: `66666666-6666-4666-8666-${String(backend.rows.length + 1).padStart(12, '0')}`, empresaId: empresa, centralId: body.centralId, origen: body.origen, estado: 0, creadaEn: new Date().toISOString(), ordenRecepcion: backend.rows.length + 1 }
    backend.rows.push(created)
    await json(created, 201)
    return
  }
  if (path === '/solicitudes') {
    if (backend.holdList) await backend.holdList
    if (backend.listStatus !== 200) { await route.fulfill({ status: backend.listStatus, body: '' }); return }
    const estado = url.searchParams.get('estado')
    const filtered = backend.rows.filter(row => !estado || states[row.estado] === estado)
    const page = Number(url.searchParams.get('page')); const size = Number(url.searchParams.get('pageSize'))
    await json({ page, pageSize: size, total: filtered.length, items: filtered.slice((page - 1) * size, page * size) })
    return
  }
  const match = /^\/solicitudes\/(.+)$/.exec(path)
  const found = match && backend.rows.find(row => row.id === match[1])
  if (found) { await json(found); return }
  await route.fulfill({ status: 404, body: '' })
}

const noOverflow = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)

test('registro rápido: Enter confirma, limpia, ilumina la fila y vuelve al campo', async ({ page }) => {
  const backend = await open(page, '/solicitudes?estado=todas&pagina=2', { rows: 21 })
  const field = page.getByLabel('Origen')
  await expect(field).toBeFocused()
  await field.fill('  Calle Sucre 210  ')
  await page.keyboard.press('Enter')
  await expect(page.getByText('Registrada · Orden 22')).toBeVisible()
  await expect(field).toHaveValue('')
  await expect(field).toBeFocused()
  expect(backend.posts).toHaveLength(1)
  // Sin punto marcado, las coordenadas viajan nulas: nunca se infieren de la dirección.
  expect(backend.posts[0].body).toEqual({ centralId: central, origen: 'Calle Sucre 210', telefonoPasajero: null, nombrePasajero: null, destino: null, observaciones: null,
    latitud: null, longitud: null, destinoLatitud: null, destinoLongitud: null })
  expect(backend.posts[0].key).toBeTruthy()
  await expect(page.locator('.queue-row.is-new')).toContainText('Calle Sucre 210')
  await field.fill('Segunda llamada')
  await page.keyboard.press('Enter')
  await expect(page.getByText('Registrada · Orden 23')).toBeVisible()
  expect(backend.posts[1].key).not.toBe(backend.posts[0].key)
  await page.setViewportSize({ width: 1366, height: 768 })
  await page.screenshot({ path: '.impeccable/review/app-confirmacion-1366.png' })
  await page.setViewportSize({ width: 390, height: 844 })
  await page.screenshot({ path: '.impeccable/review/app-confirmacion-390.png' })
})

test('resultado dudoso: reintenta con la misma clave y no duplica', async ({ page }) => {
  const backend = await open(page, '/solicitudes')
  backend.failNextPost = true
  const before = backend.rows.length
  const field = page.getByLabel('Origen')
  await field.fill('Terminal norte')
  await page.keyboard.press('Enter')
  await expect(page.getByText('No sabemos si se registró')).toBeVisible()
  await expect(field).toHaveValue('Terminal norte')
  await expect(field).not.toBeEditable()
  await expect(page.getByRole('button', { name: 'Reintentar', exact: true })).toBeFocused()
  await page.setViewportSize({ width: 390, height: 844 })
  await page.screenshot({ path: '.impeccable/review/app-dudoso-390.png' })
  await page.getByRole('button', { name: 'Reintentar', exact: true }).click()
  await expect(page.getByText(`Registrada · Orden ${before + 1}`)).toBeVisible()
  expect(backend.posts).toHaveLength(2)
  expect(backend.posts[1].key).toBe(backend.posts[0].key)
  expect(backend.posts[1].body).toEqual(backend.posts[0].body)
})

test('la solicitud nueva se ilumina aunque caiga en otra página de la cola', async ({ page }) => {
  await open(page, '/solicitudes', { rows: 21 })
  await expect(page.getByText('Página 1 de 2')).toBeVisible()
  await page.getByLabel('Origen').fill('Llamada que queda en la página 2')
  await page.keyboard.press('Enter')
  await expect(page.locator('.queue-row.is-new')).toContainText('Llamada que queda en la página 2')
  await expect(page.getByText('Página 1 de 2')).toBeVisible()
  await expect(page.locator('.queue-row.is-new')).toBeInViewport()
  await page.screenshot({ path: '.impeccable/review/app-nueva-otra-pagina.png' })
})

test('el registro no se mueve al desplazar la cola en escritorio', async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 600 })
  await open(page, '/solicitudes')
  const field = page.getByLabel('Origen')
  await expect(page.locator('.queue-origin').first()).toBeVisible()
  const before = await field.boundingBox()
  await page.evaluate(() => window.scrollTo(0, 500))
  await expect.poll(() => page.evaluate(() => window.scrollY)).toBeGreaterThan(100)
  const after = await field.boundingBox()
  expect(after?.y).toBe(before?.y)
  await page.screenshot({ path: '.impeccable/review/app-desplazado-1366.png' })
})

test('navegar conserva el borrador y la clave de un envío dudoso', async ({ page }) => {
  const backend = await open(page, '/solicitudes')
  const field = page.getByLabel('Origen')
  await field.fill('Borrador en curso')
  await page.getByRole('link', { name: 'Panel', exact: true }).click()
  await expect(page).toHaveURL(/\/panel$/)
  await expect(field).toHaveValue('Borrador en curso')
  await page.getByRole('link', { name: 'Cuenta', exact: true }).click()
  await expect(field).toBeHidden()
  await page.getByRole('link', { name: 'Solicitudes', exact: true }).click()
  await expect(field).toHaveValue('Borrador en curso')
  backend.failNextPost = true
  await field.press('Enter')
  await expect(page.getByText('No sabemos si se registró')).toBeVisible()
  await page.getByRole('link', { name: 'Panel', exact: true }).click()
  await expect(page.getByText('No sabemos si se registró')).toBeVisible()
  backend.failNextPost = true
  await page.getByRole('button', { name: 'Reintentar', exact: true }).click()
  await expect.poll(() => backend.posts.length).toBe(2)
  await expect(page.getByRole('button', { name: 'Reintentar', exact: true })).toBeEnabled()
  await expect(page.getByRole('button', { name: 'Reintentar', exact: true })).toBeFocused()
  await page.getByRole('button', { name: 'Reintentar', exact: true }).click()
  await expect(page.getByText(/Registrada · Orden/)).toBeVisible()
  expect(new Set(backend.posts.map(post => post.key)).size).toBe(1)
})

test('la entrada lleva a solicitudes y el registro sigue disponible en el detalle', async ({ page }) => {
  await open(page, '/')
  await expect(page).toHaveURL(/\/solicitudes$/)
  await page.locator('.queue-origin a').first().click()
  await expect(page.locator('.detail-head h2')).toContainText('Orden 1')
  await expect(page.getByLabel('Origen')).toBeVisible()
})

test('estados de carga, vacío y error, con capturas', async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 768 })
  let release = () => {}
  const backend = await open(page, '/solicitudes', { rows: 0 })
  await expect(page.getByText('No hay solicitudes en estado pendiente')).toBeVisible()
  await page.screenshot({ path: '.impeccable/review/app-vacio-1366.png' })
  backend.listStatus = 500
  await page.goto('https://radiomovil.invalid/solicitudes')
  await expect(page.getByText('El servicio no está disponible en este momento.')).toBeVisible()
  await page.screenshot({ path: '.impeccable/review/app-error-1366.png' })
  backend.listStatus = 200
  backend.holdList = new Promise(resolve => { release = resolve })
  await page.goto('https://radiomovil.invalid/solicitudes')
  await expect(page.getByRole('status', { name: 'Cargando' })).toBeVisible()
  await page.screenshot({ path: '.impeccable/review/app-carga-1366.png' })
  release()
})

test('validación: no envía un origen vacío', async ({ page }) => {
  const backend = await open(page, '/solicitudes')
  await page.getByRole('button', { name: 'Registrar solicitud' }).click()
  await expect(page.getByText('Escribe el origen de la solicitud.')).toBeVisible()
  expect(backend.posts).toHaveLength(0)
})

test('sin central configurada: bloquea el registro y la cola sigue disponible', async ({ page }) => {
  const backend = await open(page, '/solicitudes', { output: withoutCentral })
  await expect(page.getByText('Registro no disponible')).toBeVisible()
  await expect(page.getByLabel('Origen')).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Registrar solicitud' })).toBeDisabled()
  await expect(page.locator('.queue-row').nth(1)).toBeVisible()
  expect(backend.posts).toHaveLength(0)
  await page.setViewportSize({ width: 1366, height: 768 })
  await page.screenshot({ path: '.impeccable/review/app-solicitudes-sin-central.png' })
})

test('filtros y paginación quedan en la URL y consultan el servidor', async ({ page }) => {
  await open(page, '/solicitudes')
  await expect(page.getByText('Página 1 de 2')).toBeVisible()
  await page.getByRole('button', { name: 'Siguiente' }).click()
  await expect(page).toHaveURL(/pagina=2/)
  await expect(page.getByText('Página 2 de 2')).toBeVisible()
  await page.getByRole('button', { name: 'En viaje' }).click()
  await expect(page).toHaveURL(/estado=EnViaje/)
  await expect(page).not.toHaveURL(/pagina=/)
  await expect(page.locator('.queue-state').first()).toContainText('En viaje')
})

test('estado vacío y error con datos previos se distinguen', async ({ page }) => {
  const backend = await open(page, '/solicitudes', { rows: 0 })
  await expect(page.getByText('No hay solicitudes en estado pendiente')).toBeVisible()
  backend.rows = seed(2)
  backend.listStatus = 200
  await page.getByRole('button', { name: 'Actualizar ahora' }).click()
  await expect(page.locator('.queue-origin').first()).toBeVisible()
  backend.listStatus = 500
  await page.getByRole('button', { name: 'Actualizar ahora' }).click()
  await expect(page.getByText('Mostramos los últimos datos recibidos.')).toBeVisible()
  await expect(page.locator('.queue-origin').first()).toBeVisible()
})

test('volver a la ventana no pierde lo que el operador escribía', async ({ page }) => {
  await open(page, '/solicitudes')
  const field = page.getByLabel('Origen')
  await field.fill('Texto a medio escribir')
  await page.evaluate(() => window.dispatchEvent(new Event('focus')))
  await page.waitForTimeout(200)
  await expect(field).toHaveValue('Texto a medio escribir')
})

test('una sesión revocada lleva al login sin mostrar datos', async ({ page }) => {
  const backend = await open(page, '/solicitudes')
  await expect(page.locator('.queue-origin').first()).toBeVisible()
  backend.listStatus = 401
  backend.meStatus = 401
  await page.getByRole('button', { name: 'Actualizar ahora' }).click()
  await expect(page).toHaveURL(/\/login$/)
})

test('móvil: el menú abre, navega y se cierra con Escape', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await open(page, '/panel')
  await page.getByRole('button', { name: 'Abrir menú' }).click()
  const drawer = page.getByRole('dialog', { name: 'Menú' })
  await expect(drawer).toBeVisible()
  await page.screenshot({ path: '.impeccable/review/app-menu-390.png' })
  await page.keyboard.press('Escape')
  await expect(drawer).toBeHidden()
  await page.getByRole('button', { name: 'Abrir menú' }).click()
  await drawer.getByRole('link', { name: 'Solicitudes', exact: true }).click()
  await expect(page).toHaveURL(/\/solicitudes$/)
  await expect(drawer).toBeHidden()
})

test('solo el rol real entra: detalle y despacho muestran datos del servidor', async ({ page }) => {
  await open(page, '/solicitudes/44444444-4444-4444-8444-000000000001')
  await expect(page.getByRole('heading', { name: places[0] })).toBeVisible()
  await page.goto('https://radiomovil.invalid/solicitudes/44444444-4444-4444-8444-999999999999')
  await expect(page.getByText('No encontramos esta solicitud en tu empresa.')).toBeVisible()
  // El detalle pendiente ofrece asignación directa; Sin señal no muestra distancia inventada.
  await page.goto('https://radiomovil.invalid/solicitudes/44444444-4444-4444-8444-000000000001')
  await expect(page.getByRole('radio', { name: /Conductor sintético/ })).toBeVisible()
  await expect(page.getByText('Sin GPS reciente: distancia desconocida')).toBeVisible()
  await page.goto('https://radiomovil.invalid/despacho')
  await expect(page.getByRole('heading', { name: /Por asignar/ })).toBeVisible()
  await expect(page.getByRole('heading', { name: /Carreras activas · 0/ })).toBeVisible()
  // Asignación directa: ninguna pantalla ofrece, acepta ni rechaza.
  await expect(page.getByRole('button', { name: /ofrecer|aceptar|rechazar/i })).toHaveCount(0)
})

const sizes = [[320, 568], [390, 844], [667, 375], [768, 1024], [1024, 768], [1366, 768], [1920, 1080]] as const
const routes = [['panel', '/panel'], ['solicitudes', '/solicitudes'], ['detalle', '/solicitudes/44444444-4444-4444-8444-000000000005'], ['despacho', '/despacho'], ['cuenta', '/cuenta']] as const

test('responsive: sin desbordes en ningún tamaño, con capturas para revisar', async ({ page }) => {
  test.setTimeout(120_000)
  const errors: string[] = []
  page.on('pageerror', error => errors.push(error.message))
  for (const [name, path] of routes) {
    for (const [width, height] of sizes) {
      await page.unrouteAll({ behavior: 'ignoreErrors' })
      await page.setViewportSize({ width, height })
      await open(page, path)
      await expect(page.locator('.app-topbar h1')).toBeVisible()
      await page.waitForLoadState('networkidle')
      expect(await noOverflow(page), `${name} a ${width}x${height}`).toBe(true)
      // Una sola navegación por tamaño: sidebar visible o botón de menú, nunca ambos.
      expect(await page.getByRole('button', { name: 'Abrir menú' }).isVisible(), `menú a ${width}`).toBe(width < 1024)
      expect(await page.locator('.app-sidebar').isVisible(), `sidebar a ${width}`).toBe(width >= 1024)
      const small = await page.evaluate(() => [...document.querySelectorAll('.app-shell button, .app-shell a, .app-shell input')]
        // Un radio o casilla dentro de su <label> se activa tocando toda la etiqueta: se mide esa área real.
        .map(element => ({ element, target: element.matches('input[type=radio], input[type=checkbox]') && element.closest('label') ? element.closest('label')! : element }))
        .filter(({ element, target }) => { const box = target.getBoundingClientRect(); return box.width > 0 && box.height > 0 && box.height < 44 && !element.closest('dialog') && !element.classList.contains('skip-link') && !element.closest('.queue-origin') })
        .map(({ element }) => element)
        .map(element => element.textContent || element.getAttribute('aria-label')))
      expect(small, `${name} a ${width}: objetivos pequeños`).toEqual([])
      await page.screenshot({ path: `.impeccable/review/app-${name}-${width}.png` })
    }
  }
  expect(errors).toEqual([])
})
