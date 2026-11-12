import { useEffect, useRef, useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useMatches } from 'react-router-dom'
import { useAuth } from '../features/auth/auth-context'
import { Icon } from '../shared/components/Icon'
import type { IconName } from '../shared/components/Icon'
import type { CompanyRole } from '../features/auth/session'

// Los permisos los valida el backend; ocultar una opción no es autorización.
const navigation: { to: string; label: string; icon: IconName; roles: CompanyRole[] }[] = [
  { to: '/solicitudes', label: 'Solicitudes', icon: 'solicitudes', roles: ['Administrador', 'Operador'] },
  { to: '/panel', label: 'Panel', icon: 'panel', roles: ['Administrador', 'Operador'] },
  { to: '/despacho', label: 'Despacho', icon: 'despacho', roles: ['Administrador', 'Operador'] },
  { to: '/flota', label: 'Flota', icon: 'flota', roles: ['Administrador', 'Operador'] },
  { to: '/cuenta', label: 'Cuenta', icon: 'cuenta', roles: ['Administrador', 'Operador'] },
]

function Brand() {
  return <Link className="app-brand" to="/solicitudes" aria-label="RadioMovil, solicitudes">
    <svg viewBox="0 0 32 32" aria-hidden="true" className="brand-mark"><rect x="2" y="2" width="28" height="28" rx="7" fill="#fff" /><path d="M10 23V9h7a4 4 0 0 1 0 8h-7m7 0 5 6" fill="none" stroke="#123551" strokeWidth="2.5" strokeLinejoin="round" /></svg>
    <span className="app-brand-name">Radio<span className="wordmark-light">Movil</span></span>
  </Link>
}

export function AppLayout() {
  const { state, busy, logout } = useAuth()
  const location = useLocation()
  const matches = useMatches()
  const title = (matches.at(-1)?.handle as { title?: string } | undefined)?.title ?? 'RadioMovil'
  const drawer = useRef<HTMLDialogElement>(null)
  const [open, setOpen] = useState(false)
  const roles = state.session?.roles ?? []
  const items = navigation.filter(item => item.roles.some(role => roles.includes(role)))

  useEffect(() => { document.title = `${title} · RadioMovil` }, [title])
  useEffect(() => { drawer.current?.close() }, [location.pathname])
  useEffect(() => { if (open) drawer.current?.showModal(); else drawer.current?.close() }, [open])

  const links = (onNavigate?: () => void) => <ul>{items.map(item => <li key={item.to}>
    <NavLink to={item.to} title={item.label} onClick={onNavigate}><Icon name={item.icon} /><span className="nav-label">{item.label}</span></NavLink>
  </li>)}</ul>

  return <div className="app-shell">
    <a className="skip-link" href="#app-content">Saltar al contenido</a>
    <aside className="app-sidebar"><Brand /><nav aria-label="Principal">{links()}</nav></aside>
    <dialog ref={drawer} className="app-drawer" aria-label="Menú" onClose={() => setOpen(false)}
      onClick={event => { if (event.target === drawer.current) setOpen(false) }}>
      <div className="app-drawer-panel">
        <div className="app-drawer-head"><Brand />
          <button type="button" className="icon-button icon-button-dark" aria-label="Cerrar menú" onClick={() => setOpen(false)}><Icon name="close" /></button></div>
        <nav aria-label="Principal">{links(() => setOpen(false))}</nav>
      </div>
    </dialog>
    <div className="app-main">
      <header className="app-topbar">
        <button type="button" className="icon-button app-menu-button" aria-label="Abrir menú" aria-expanded={open} onClick={() => setOpen(true)}><Icon name="menu" /></button>
        <h1>{title}</h1>
        <div className="app-account">
          <span className="app-account-info"><span className="app-account-email">{state.session?.email}</span><span className="app-account-role">{roles.join(', ')}</span></span>
          <button type="button" className="icon-button" aria-label="Cerrar sesión" title="Cerrar sesión" disabled={busy} onClick={() => void logout()}><Icon name="logout" /></button>
        </div>
      </header>
      <main id="app-content" className="app-content" tabIndex={-1}><Outlet /></main>
    </div>
  </div>
}
