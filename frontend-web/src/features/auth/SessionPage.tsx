import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from './auth-context'
import { AuthFrame } from './AuthFrame'
import { homeFor } from './session'
import type { CompanyRole } from './session'

export function SessionState() {
  const { state, busy, restore, logout } = useAuth()
  return <div className="form-content" aria-busy={busy}>
    <h2>{state.status === 'checking' ? 'Comprobando tu sesión…' : state.status === 'denied' ? 'Acceso no disponible' : 'No pudimos comprobar tu sesión'}</h2>
    <p className="state-description" role="status">{state.status === 'checking' ? 'Un momento, estamos verificando tu acceso.' : state.message}</p>
    {state.status !== 'checking' && <div className="state-actions">
      <button className="primary-button" onClick={() => void restore()} disabled={busy}>Volver a comprobar</button>
      {state.status === 'denied' && <button className="secondary-button" onClick={() => void logout()} disabled={busy}>{busy ? 'Procesando…' : 'Cerrar sesión'}</button>}
    </div>}
  </div>
}

export function ProtectedRoute({ roles }: { roles: CompanyRole[] }) {
  const { state } = useAuth()
  if (state.status === 'anonymous') return <Navigate to="/login" replace />
  if (state.status !== 'authenticated') return <AuthFrame><SessionState /></AuthFrame>
  // Ocultar rutas no es autorización: el backend valida cada operación.
  if (!state.session?.roles.some(role => roles.includes(role))) return <Navigate to={homeFor(state.session)} replace />
  return <Outlet />
}
