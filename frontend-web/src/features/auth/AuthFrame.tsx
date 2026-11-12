import type { ReactNode } from 'react'

export function AuthFrame({ children }: { children: ReactNode }) {
  return <div className="auth-page">
    <a className="skip-link" href="#main-content">Saltar al contenido</a>
    <header className="brand-header">
      <a className="wordmark" href="/login" aria-label="RadioMovil, inicio">
        <svg viewBox="0 0 32 32" aria-hidden="true" className="brand-mark"><rect x="2" y="2" width="28" height="28" rx="7" fill="currentColor" /><path d="M10 23V9h7a4 4 0 0 1 0 8h-7m7 0 5 6" fill="none" stroke="white" strokeWidth="2.5" strokeLinejoin="round" /></svg>
        <span>Radio<span className="wordmark-light">Movil</span></span>
      </a>
      <span className="header-caption">Acceso empresarial</span>
    </header>
    <div className="auth-grid">
      <aside className="intro-panel" aria-label="RadioMovil para tu central">
        <div className="intro-content">
          <h1>Tu central. <br />Todo empieza aquí.</h1>
          <p>Un espacio de trabajo para los administradores y operadores de RadioMovil.</p>
          <div className="intro-note"><span className="note-line" aria-hidden="true" /><span>Entra con la cuenta asignada<br />por tu empresa.</span></div>
        </div>
        <div className="intro-footer">Conecta con tu jornada.</div>
      </aside>
      <main id="main-content" className="form-panel" tabIndex={-1}>{children}</main>
    </div>
    <footer className="page-footer"><span>RadioMovil · Plataforma empresarial</span><span>Acceso exclusivo para personal autorizado</span></footer>
  </div>
}
