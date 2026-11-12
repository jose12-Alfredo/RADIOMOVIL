import { useAuth } from './auth-context'
import { formatTime } from '../../shared/format'

export function AccountPage() {
  const { state, busy, logout } = useAuth()
  if (!state.session) return null
  return <div className="single-column account-page">
    <section aria-labelledby="account-title">
      <div className="section-head"><h2 id="account-title">Tu cuenta</h2></div>
      <dl className="detail-facts">
        <div><dt>Cuenta</dt><dd>{state.session.email ?? 'Correo no disponible'}</dd></div>
        <div><dt>Rol empresarial</dt><dd>{state.session.roles.join(', ')}</dd></div>
        <div><dt>Sesión válida hasta</dt><dd><time dateTime={state.session.expiraEn}>{formatTime(state.session.expiraEn)}</time></dd></div>
      </dl>
      {state.message && <div className="work-notice work-notice-error" role="alert"><div className="work-notice-body"><p>{state.message}</p></div></div>}
      <button type="button" className="secondary-button account-logout" disabled={busy} onClick={() => void logout()}>{busy ? 'Cerrando sesión…' : 'Cerrar sesión'}</button>
    </section>
  </div>
}
