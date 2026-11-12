import { expect, test } from '@playwright/test'
import { build } from 'vite'
import { readFile, mkdir } from 'node:fs/promises'
import { resolve } from 'node:path'

const output = resolve('test-results/offline-build')
test.beforeAll(async () => {
  // Build aislado SIN empresa: no inventa usuarios, empresas ni sesiones.
  await build({ define: { 'import.meta.env.VITE_EMPRESA_ID': 'undefined' },
    build: { outDir: output, emptyOutDir: true }, logLevel: 'warn' })
  await mkdir('.impeccable/review', { recursive: true })
})

test.beforeEach(async ({ page }) => {
  // El navegador renderiza el bundle real con transporte interceptado localmente.
  // No hay servidor remoto, excepción de certificados ni validación TLS simulada.
  await page.route('**/*', async route => {
    const url = new URL(route.request().url())
    if (url.hostname !== 'radiomovil.invalid' || url.pathname.startsWith('/api/')) {
      await route.abort('connectionrefused'); return
    }
    const path = url.pathname.startsWith('/assets/') || url.pathname === '/radiomovil.svg' ? url.pathname : '/index.html'
    const file = resolve(output, `.${path}`)
    if (!file.startsWith(output)) { await route.abort(); return }
    await route.fulfill({ status: 200, body: await readFile(file), contentType:
      path.endsWith('.js') ? 'application/javascript' : path.endsWith('.css') ? 'text/css' : path.endsWith('.svg') ? 'image/svg+xml' : 'text/html' })
  })
})

for (const [name, width, height] of [['desktop', 1440, 1000], ['tablet', 820, 1180], ['mobile', 390, 844]] as const) {
  test(`login ${name}: configuración pendiente, responsive y sin autenticación ficticia`, async ({ page }) => {
    const errors: string[] = []
    page.on('pageerror', error => errors.push(error.message))
    page.on('console', message => { if (message.type() === 'error') errors.push(message.text()) })
    await page.setViewportSize({ width, height })
    await page.goto('https://radiomovil.invalid/login')
    await expect(page.getByRole('heading', { name: 'Iniciar sesión', exact: true })).toBeVisible()
    await expect(page.getByText('Configuración pendiente', { exact: true })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Entrar a RadioMovil' })).toBeDisabled()
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
    await page.screenshot({ path: `.impeccable/review/${name}.png`, fullPage: true })
    expect(errors).toEqual([])
  })
}

test('teclado, contraseña visible y ruta protegida sin sesión', async ({ page }) => {
  const apiCalls: string[] = []
  page.on('request', request => { if (new URL(request.url()).pathname.startsWith('/api/')) apiCalls.push(request.url()) })
  await page.goto('https://radiomovil.invalid/session')
  await expect(page).toHaveURL('https://radiomovil.invalid/login')
  await page.keyboard.press('Tab')
  await expect(page.getByRole('link', { name: 'Saltar al contenido' })).toBeFocused()
  await page.keyboard.press('Enter')
  await expect(page.locator('#main-content')).toBeFocused()
  await page.getByLabel('Contraseña', { exact: true }).fill('texto de comprobación local')
  await page.getByRole('button', { name: 'Mostrar contraseña' }).focus()
  await page.keyboard.press('Space')
  await expect(page.getByLabel('Contraseña', { exact: true })).toHaveAttribute('type', 'text')
  await page.keyboard.press('Space')
  await expect(page.getByLabel('Contraseña', { exact: true })).toHaveAttribute('type', 'password')
  await page.setViewportSize({ width: 320, height: 640 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
  expect(apiCalls).toEqual([])
})
