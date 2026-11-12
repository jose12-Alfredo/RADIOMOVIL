import { test } from 'node:test'
import assert from 'node:assert/strict'
import { authErrorMessage, validateLogin } from '../src/features/auth/auth-errors.ts'
import { HttpError, NetworkError } from '../src/services/http.ts'

test('valida requeridos y correo sin imponer políticas nuevas a contraseñas existentes', () => {
  assert.deepEqual(Object.keys(validateLogin('', '')), ['email', 'password'])
  assert.ok(validateLogin('incorrecto', 'clave').email)
  assert.ok(validateLogin('nombre@example.invalid', 'a'.repeat(257)).password)
  assert.deepEqual(validateLogin(' nombre@example.invalid ', 'clave'), {})
})

test('401 distingue credenciales de sesión perdida y no expone respuesta del servidor', () => {
  const error = new HttpError(401, 'secret backend diagnostic')
  assert.match(authErrorMessage(error, 'login'), /correo y contraseña/)
  assert.match(authErrorMessage(error, 'session'), /sesión ya no está disponible/)
  assert.doesNotMatch(authErrorMessage(error, 'login'), /secret/)
})

test('errores de red, CSRF/validación y acceso tienen recuperación explícita', () => {
  assert.match(authErrorMessage(new NetworkError(null), 'login'), /conexión/)
  assert.match(authErrorMessage(new HttpError(403, null), 'session'), /no tiene acceso/)
  assert.match(authErrorMessage(new HttpError(400, null), 'login'), /validar la petición/)
  assert.match(authErrorMessage(new HttpError(500, null), 'login'), /servicio no está disponible/)
  assert.match(authErrorMessage(new Error(), 'logout'), /No pudimos confirmar/)
})
