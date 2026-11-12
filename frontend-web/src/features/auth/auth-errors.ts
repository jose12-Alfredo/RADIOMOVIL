import { HttpError, NetworkError } from '../../services/http.ts'

export function authErrorMessage(error: unknown, operation: 'login' | 'session' | 'logout'): string {
  if (error instanceof NetworkError) return 'No pudimos conectar con el servidor. Comprueba tu conexión y vuelve a intentarlo.'
  if (error instanceof HttpError) {
    if (error.status === 401) return operation === 'login'
      ? 'No se pudo iniciar sesión. Revisa tu correo y contraseña o consulta con tu administrador.'
      : 'La sesión ya no está disponible. Vuelve a iniciar sesión.'
    if (error.status === 403) return 'Tu cuenta no tiene acceso a este espacio. Contacta con tu administrador.'
    if (error.status === 400) return 'No se pudo validar la petición. Vuelve a intentarlo; si continúa, contacta con tu administrador.'
    if (error.status === 429) return 'Se han realizado demasiados intentos. Espera unos minutos antes de volver a intentarlo.'
    if (error.status >= 500) return 'El servicio no está disponible en este momento. Inténtalo de nuevo más tarde.'
  }
  return operation === 'logout'
    ? 'No pudimos confirmar el cierre de sesión. Vuelve a intentarlo.'
    : 'No pudimos verificar la respuesta del servicio. Vuelve a intentarlo o contacta con tu administrador.'
}

export function validateLogin(email: string, password: string): { email?: string; password?: string } {
  const errors: { email?: string; password?: string } = {}
  if (!email.trim()) errors.email = 'Introduce tu correo electrónico.'
  else if (email.trim().length > 256 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) errors.email = 'Introduce un correo electrónico válido.'
  if (!password) errors.password = 'Introduce tu contraseña.'
  else if (password.length > 256) errors.password = 'La contraseña no puede superar los 256 caracteres.'
  return errors
}
