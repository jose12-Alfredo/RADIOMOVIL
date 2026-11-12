import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Navigate } from 'react-router-dom'
import { useAuth } from './auth-context'
import { AuthFrame } from './AuthFrame'
import { validateLogin } from './auth-errors'
import { SessionState } from './SessionPage'
import { homeFor } from './session'

export function LoginPage() {
  const { state, configured, busy, login, restore } = useAuth()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [visible, setVisible] = useState(false)
  const [errors, setErrors] = useState<{ email?: string; password?: string }>({})
  const emailRef = useRef<HTMLInputElement>(null)
  const passwordRef = useRef<HTMLInputElement>(null)
  useEffect(() => { document.title = 'Iniciar sesión · RadioMovil' }, [])

  if (state.status === 'authenticated') return <Navigate to={homeFor(state.session)} replace />
  if (state.status === 'checking' || state.status === 'denied') return <AuthFrame><SessionState /></AuthFrame>

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (busy || !configured || state.status === 'uncertain') return
    const issues = validateLogin(email, password)
    setErrors(issues)
    if (issues.email) { emailRef.current?.focus(); return }
    if (issues.password) { passwordRef.current?.focus(); return }
    const submitted = password
    setPassword('')
    await login(email, submitted)
  }

  return <AuthFrame><div className="form-content">
    <div className="form-heading"><h2>Iniciar sesión</h2><p>Bienvenido a tu espacio de trabajo.</p></div>
    {!configured && <div className="notice" role="status"><strong>Configuración pendiente</strong><p>El acceso de esta instalación aún no está habilitado. Contacta con tu administrador para configurar la empresa y la conexión.</p></div>}
    {state.message && <div className={state.messageTone === 'info' ? 'notice' : 'notice notice-error'} role={state.messageTone === 'info' ? 'status' : 'alert'}><p>{state.message}</p></div>}
    {state.status === 'uncertain' && <button type="button" className="secondary-button retry-button" onClick={() => void restore()} disabled={busy}>Comprobar sesión</button>}
    <form noValidate onSubmit={submit} aria-busy={busy}>
      <div className="field"><label htmlFor="email">Correo electrónico</label>
        <input ref={emailRef} id="email" name="email" type="email" autoComplete="username" inputMode="email" spellCheck={false} autoCapitalize="none" maxLength={256} required placeholder="nombre@tuempresa.com" value={email} onChange={event => setEmail(event.target.value)} aria-invalid={!!errors.email} aria-describedby={errors.email ? 'email-error' : undefined} disabled={busy} />
        {errors.email && <p className="field-error" id="email-error">{errors.email}</p>}
      </div>
      <div className="field"><label htmlFor="password">Contraseña</label>
        <div className="password-field"><input ref={passwordRef} id="password" name="password" type={visible ? 'text' : 'password'} autoComplete="current-password" required maxLength={256} value={password} onChange={event => setPassword(event.target.value)} aria-invalid={!!errors.password} aria-describedby={errors.password ? 'password-error' : undefined} disabled={busy} />
          <button type="button" className="password-toggle" aria-label={visible ? 'Ocultar contraseña' : 'Mostrar contraseña'} aria-pressed={visible} onClick={() => setVisible(value => !value)} disabled={busy}>{visible ? 'Ocultar' : 'Mostrar'}</button>
        </div>
        {errors.password && <p className="field-error" id="password-error">{errors.password}</p>}
      </div>
      <button className="primary-button" type="submit" disabled={busy || !configured || state.status === 'uncertain'}>{busy ? 'Verificando acceso…' : 'Entrar a RadioMovil'}<svg className="button-arrow" viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12h14m-6-6 6 6-6 6" /></svg></button>
      <p className="form-help">Utiliza tus credenciales empresariales. Si necesitas acceso o recuperar tu contraseña, contacta con tu administrador.</p>
    </form>
    <div className="form-bottom"><svg viewBox="0 0 24 24" aria-hidden="true"><rect x="5" y="10" width="14" height="11" rx="2" /><path d="M8 10V7a4 4 0 0 1 8 0v3M12 14v3" /></svg><span>Tu acceso está vinculado a tu empresa.</span></div>
  </div></AuthFrame>
}
