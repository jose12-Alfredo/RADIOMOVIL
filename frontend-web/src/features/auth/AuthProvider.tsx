import { useCallback, useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { HttpError } from '../../services/http'
import { AuthContext } from './auth-context'
import type { AuthState } from './auth-context'
import { authErrorMessage } from './auth-errors'
import { getSession, loginWeb, logoutWeb, resolveCompanyId, SessionLookupError } from './session'
import type { Session } from './session'

function installationCompany(): string | null {
  try { return resolveCompanyId(import.meta.env.VITE_EMPRESA_ID) }
  catch { return null }
}
const company = installationCompany()
// Cada rol entra solo a su espacio (ver ProtectedRoute); la empresa debe ser la de esta instalación.
function sessionAllowed(session: Session): boolean {
  return session.empresaId.toLowerCase() === company?.toLowerCase() &&
    session.roles.some(role => role === 'Administrador' || role === 'Operador' || role === 'Conductor')
}
const initial: AuthState = { status: company ? 'checking' : 'anonymous', session: null, message: '' }

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>(initial)
  const [busy, setBusy] = useState(false)
  const active = useRef<AbortController | null>(null)
  const knownSession = useRef(false)

  const acceptSession = useCallback((session: Session) => {
    knownSession.current = true
    const allowed = sessionAllowed(session)
    setState({ status: allowed ? 'authenticated' : 'denied', session,
      message: allowed ? '' : 'Esta sesión no tiene acceso al espacio empresarial configurado. Puedes cerrar sesión y utilizar una cuenta autorizada.' })
  }, [])

  const restore = useCallback(async () => {
    if (!company || active.current) return
    const controller = new AbortController()
    active.current = controller
    setState({ status: 'checking', session: null, message: '' })
    setBusy(true)
    try {
      const session = await getSession(controller.signal)
      if (!controller.signal.aborted) acceptSession(session)
    }
    catch (error) {
      if (controller.signal.aborted) return
      const unauthorized = error instanceof HttpError && error.status === 401
      const denied = error instanceof HttpError && error.status === 403
      setState({ status: unauthorized ? 'anonymous' : denied ? 'denied' : 'uncertain', session: null,
        message: unauthorized && !knownSession.current ? '' : authErrorMessage(error, 'session') })
      if (unauthorized) knownSession.current = false
    } finally {
      if (active.current === controller) { active.current = null; setBusy(false) }
    }
  }, [acceptSession])

  useEffect(() => {
    // Sincroniza con la sesión HTTP externa al montar; no es estado derivado de props.
    // oxlint-disable-next-line react/set-state-in-effect
    void restore()
    return () => { active.current?.abort(); active.current = null }
  }, [restore])

  // Revalida sin desmontar la aplicación: una ventana que recupera el foco no debe perder
  // lo que el operador estaba escribiendo. Solo la respuesta del servidor cambia el estado.
  const revalidate = useCallback(async () => {
    if (active.current) return
    const controller = new AbortController()
    active.current = controller
    try {
      const session = await getSession(controller.signal)
      if (controller.signal.aborted) return
      const allowed = sessionAllowed(session)
      if (!allowed) acceptSession(session)
      else setState(previous => previous.session && previous.session.expiraEn === session.expiraEn &&
        previous.session.roles[0] === session.roles[0] ? previous : { status: 'authenticated', session, message: '' })
    } catch (error) {
      if (controller.signal.aborted) return
      if (error instanceof HttpError && (error.status === 401 || error.status === 403)) {
        const unauthorized = error.status === 401
        if (unauthorized) knownSession.current = false
        setState({ status: unauthorized ? 'anonymous' : 'denied', session: null, message: authErrorMessage(error, 'session') })
      }
      // Un fallo de red no demuestra que la sesión terminó: se reintentará en el próximo foco.
    } finally {
      if (active.current === controller) active.current = null
    }
  }, [acceptSession])

  useEffect(() => {
    if (!state.session) return
    // El servidor es autoridad temporal; evita bucles si el reloj local está adelantado.
    const delay = Math.max(30_000, Date.parse(state.session.expiraEn) - Date.now())
    const timer = window.setTimeout(() => { void revalidate() }, Math.min(delay, 2_147_483_647))
    const recheck = () => { if (document.visibilityState === 'visible') void revalidate() }
    window.addEventListener('focus', recheck)
    return () => { clearTimeout(timer); window.removeEventListener('focus', recheck) }
  }, [state.session, revalidate])

  async function login(email: string, password: string) {
    if (!company || active.current) return
    const controller = new AbortController()
    active.current = controller
    setBusy(true)
    setState({ status: 'anonymous', session: null, message: '' })
    try {
      const session = await loginWeb(email.trim(), password, controller.signal)
      if (!controller.signal.aborted) acceptSession(session)
    }
    catch (error) {
      if (controller.signal.aborted) return
      setState({ status: error instanceof SessionLookupError ? 'uncertain' : 'anonymous', session: null,
        message: error instanceof SessionLookupError
          ? 'El acceso fue aceptado, pero no pudimos recuperar tu sesión. Comprueba la sesión antes de volver a enviar tus credenciales.'
          : authErrorMessage(error, 'login') })
    } finally {
      if (active.current === controller) { active.current = null; setBusy(false) }
    }
  }

  async function logout() {
    if (active.current) return
    const controller = new AbortController()
    active.current = controller
    setBusy(true)
    try {
      await logoutWeb(controller.signal)
      if (controller.signal.aborted) return
      knownSession.current = false
      setState({ status: 'anonymous', session: null, message: 'Has cerrado sesión.', messageTone: 'info' })
    } catch (error) {
      if (controller.signal.aborted) return
      if (error instanceof HttpError && error.status === 401) {
        knownSession.current = false
        setState({ status: 'anonymous', session: null, message: 'La sesión ya no está disponible. Vuelve a iniciar sesión.' })
      } else setState(previous => ({ ...previous, message: authErrorMessage(error, 'logout') }))
    } finally {
      if (active.current === controller) { active.current = null; setBusy(false) }
    }
  }

  return <AuthContext.Provider value={{ state, busy, configured: company !== null, restore, revalidate, login, logout }}>{children}</AuthContext.Provider>
}
