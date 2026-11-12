import { useCallback, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useApiQuery } from '../../shared/hooks/useApiQuery'
import { ErrorState, SkeletonRows } from '../../shared/components/feedback'
import { Icon } from '../../shared/components/Icon'
import { ActionNotice } from './ActionNotice'
import { useIdempotentAction } from './useIdempotentAction'
import { authorizeVehicle, classifyActionError, createDriverAccess, describeFleetLoadError, listAuthorizations, revokeAuthorization, validateAccessPassword } from './fleet'
import type { Driver, Vehicle } from './fleet'

/** Solo Administrador (el backend lo exige). Acceso del conductor y vehículos con los que puede iniciar turno. */
export function DriverAccessPanel({ driver, vehicles, onChanged }: { driver: Driver; vehicles: Vehicle[]; onChanged: () => void }) {
  const authorizations = useApiQuery(signal => listAuthorizations(driver.id, signal), `autorizaciones:${driver.id}`, 0)
  const [vehicle, setVehicle] = useState('')
  const refresh = authorizations.refresh
  const authorize = useIdempotentAction(authorizeVehicle, useCallback(() => { setVehicle(''); refresh() }, [refresh]))
  const revoke = useIdempotentAction(revokeAuthorization, useCallback(() => refresh(), [refresh]))
  const authorized = new Set(authorizations.data?.map(a => a.vehiculoId))
  const candidates = vehicles.filter(v => !authorized.has(v.id))

  return <div className="access-panel">
    <section aria-labelledby={`acceso-${driver.id}`}>
      <h3 id={`acceso-${driver.id}`}>Acceso a la app</h3>
      {driver.accesoEmail
        ? <p className="access-line"><Icon name="check" /><span>Acceso activo: <strong>{driver.accesoEmail}</strong></span></p>
        : <AccessForm driver={driver} onCreated={onChanged} />}
    </section>
    <section aria-labelledby={`vehiculos-${driver.id}`}>
      <h3 id={`vehiculos-${driver.id}`}>Vehículos autorizados</h3>
      <p className="fleet-sub">Con estos vehículos {driver.nombre} puede iniciar turno desde su teléfono.</p>
      {authorizations.error != null && <ErrorState message={describeFleetLoadError(authorizations.error)} onRetry={refresh} busy={authorizations.refreshing} />}
      {authorizations.loading && <SkeletonRows rows={1} />}
      {authorizations.data && authorizations.data.length === 0 && <p className="fleet-sub">Ninguno todavía.</p>}
      {authorizations.data && authorizations.data.length > 0 && <ul className="auth-list">{authorizations.data.map(a => <li key={a.id}>
        <span className="plate">{a.patente}</span>
        <button type="button" className="text-action" disabled={revoke.locked} onClick={() => revoke.submit({ autorizacionId: a.id })}>Revocar</button>
      </li>)}</ul>}
      <ActionNotice state={revoke.state} onRetry={revoke.retry} onDiscard={() => { revoke.reset(); refresh() }} discardLabel="Descartar y comprobar" />
      {candidates.length > 0 && <form className="inline-form" noValidate aria-busy={authorize.sending}
        onSubmit={event => { event.preventDefault(); if (!authorize.locked && vehicle) authorize.submit({ conductorId: driver.id, vehiculoId: vehicle }) }}>
        <div className="field"><label htmlFor={`autorizar-${driver.id}`}>Autorizar vehículo</label>
          <select id={`autorizar-${driver.id}`} value={authorize.state.status === 'uncertain' ? authorize.state.pending.input.vehiculoId : vehicle} disabled={authorize.locked}
            onChange={event => { setVehicle(event.target.value); if (authorize.state.status === 'rejected') authorize.reset() }}>
            <option value="">Elige un vehículo</option>
            {candidates.map(v => <option key={v.id} value={v.id}>{v.patente}</option>)}
          </select></div>
        {authorize.state.status !== 'uncertain' && <div className="inline-actions">
          <button type="submit" className="primary-button" disabled={authorize.sending || !vehicle}>{authorize.sending ? 'Autorizando…' : 'Autorizar'}</button>
        </div>}
        <ActionNotice state={authorize.state} onRetry={authorize.retry} onDiscard={() => { authorize.reset(); refresh() }} discardLabel="Descartar y comprobar" />
      </form>}
    </section>
  </div>
}

function AccessForm({ driver, onCreated }: { driver: Driver; onCreated: () => void }) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [visible, setVisible] = useState(false)
  const [errors, setErrors] = useState<{ email?: string; password?: string }>({})
  const [result, setResult] = useState<{ tone: 'error' | 'warning'; message: string } | null>(null)
  const [sending, setSending] = useState(false)
  const emailRef = useRef<HTMLInputElement>(null)

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (sending) return
    const issues = { email: /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim()) ? undefined : 'Escribe un correo válido.', password: validateAccessPassword(password) ?? undefined }
    setErrors(issues)
    if (issues.email || issues.password) { emailRef.current?.focus(); return }
    setSending(true); setResult(null)
    try {
      await createDriverAccess({ conductorId: driver.id, email: email.trim(), password })
      setPassword('')
      onCreated()
    } catch (error) {
      const failure = classifyActionError(error)
      // Sin clave idempotente en este endpoint: ante duda, comprobar la lista antes de repetir.
      setResult(failure.kind === 'uncertain'
        ? { tone: 'warning', message: `${failure.message} Actualiza la lista para comprobar si el acceso se creó antes de repetir.` }
        : { tone: 'error', message: failure.kind === 'rejected' && failure.message === 'El servidor rechazó los datos.' ? 'El servidor rechazó el correo o la contraseña (¿correo ya usado?).' : failure.message })
    } finally { setSending(false) }
  }

  return <form className="access-form" noValidate onSubmit={event => void submit(event)} aria-busy={sending}>
    <p className="fleet-sub">Crea el usuario con el que {driver.nombre} entrará desde su teléfono. Entrégale la contraseña inicial en persona.</p>
    <div className="field"><label htmlFor={`acceso-email-${driver.id}`}>Correo</label>
      <input ref={emailRef} id={`acceso-email-${driver.id}`} type="email" autoComplete="off" spellCheck={false} autoCapitalize="none" maxLength={256}
        value={email} onChange={e => setEmail(e.target.value)} disabled={sending} aria-invalid={!!errors.email} aria-describedby={errors.email ? `acceso-email-error-${driver.id}` : undefined} />
      {errors.email && <p className="field-error" id={`acceso-email-error-${driver.id}`}>{errors.email}</p>}</div>
    <div className="field"><label htmlFor={`acceso-clave-${driver.id}`}>Contraseña inicial</label>
      <div className="password-field"><input id={`acceso-clave-${driver.id}`} type={visible ? 'text' : 'password'} autoComplete="new-password" maxLength={256}
        value={password} onChange={e => setPassword(e.target.value)} disabled={sending} aria-invalid={!!errors.password}
        aria-describedby={errors.password ? `acceso-clave-error-${driver.id}` : `acceso-clave-ayuda-${driver.id}`} />
        <button type="button" className="password-toggle" aria-label={visible ? 'Ocultar contraseña' : 'Mostrar contraseña'} aria-pressed={visible} onClick={() => setVisible(v => !v)}>{visible ? 'Ocultar' : 'Mostrar'}</button></div>
      {errors.password ? <p className="field-error" id={`acceso-clave-error-${driver.id}`}>{errors.password}</p>
        : <p className="field-help" id={`acceso-clave-ayuda-${driver.id}`}>Mínimo 12 caracteres, con mayúscula, número y símbolo.</p>}</div>
    <button type="submit" className="primary-button" disabled={sending}>{sending ? 'Creando acceso…' : 'Crear acceso'}</button>
    {result && <div className={`work-notice work-notice-${result.tone}`} role="alert"><Icon name="alert" /><div className="work-notice-body"><p>{result.message}</p></div></div>}
  </form>
}
