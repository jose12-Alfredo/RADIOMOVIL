import { useCallback, useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useAuth } from '../auth/auth-context'
import { useApiQuery } from '../../shared/hooks/useApiQuery'
import { ErrorState, SkeletonRows } from '../../shared/components/feedback'
import { Icon } from '../../shared/components/Icon'
import { formatTime } from '../../shared/format'
import { ActionNotice } from '../fleet/ActionNotice'
import { DriverStateBadge } from '../fleet/DriversPage'
import { useIdempotentAction } from '../fleet/useIdempotentAction'
import { closeReasonMaxLength, describeFleetLoadError, gpsStateLabel } from '../fleet/fleet'
import type { GpsState } from '../fleet/fleet'
import { closeMyShift, getMe, listMyVehicles, startMyShift } from './driver'
import type { Me } from './driver'
import { useGpsEmitter } from './useGpsEmitter'

export function GpsBadge({ state }: { state: GpsState }) {
  return <span className={`state-badge gps-${state.toLowerCase()}`}>{gpsStateLabel[state]}</span>
}

/** Herramienta TEMPORAL de prueba: el navegador del teléfono como emisor GPS. La app definitiva será Flutter. */
export function DriverGpsPage() {
  const { state, busy, logout } = useAuth()
  const me = useApiQuery(getMe, 'conductor-yo', 15_000)
  useEffect(() => { document.title = 'Mi turno · RadioMovil' }, [])
  const data = me.data

  return <div className="driver-app">
    <header className="driver-top">
      <span className="driver-brand"><svg viewBox="0 0 32 32" aria-hidden="true" className="brand-mark"><rect x="2" y="2" width="28" height="28" rx="7" fill="#fff" /><path d="M10 23V9h7a4 4 0 0 1 0 8h-7m7 0 5 6" fill="none" stroke="#123551" strokeWidth="2.5" strokeLinejoin="round" /></svg>
        <span>Radio<span className="wordmark-light">Movil</span></span></span>
      <span className="driver-account">{state.session?.email}</span>
      <button type="button" className="icon-button icon-button-dark" aria-label="Cerrar sesión" title="Cerrar sesión" disabled={busy} onClick={() => void logout()}><Icon name="logout" /></button>
    </header>
    <main className="driver-main" id="app-content">
      {me.error != null && <ErrorState message={describeFleetLoadError(me.error)} onRetry={me.refresh} busy={me.refreshing} />}
      {me.loading && <SkeletonRows rows={3} />}
      {data && <>
        <section className="driver-card" aria-labelledby="driver-name">
          <div className="driver-head"><h1 id="driver-name">{data.nombre}</h1><DriverStateBadge state={data.estado} /></div>
          <p className="driver-line">{data.turno
            ? <>Turno con <span className="plate">{data.turno.patente}</span> desde <time dateTime={data.turno.inicio}>{formatTime(data.turno.inicio)}</time></>
            : 'Sin turno abierto.'}</p>
        </section>
        {data.turno ? <ShiftOpen me={data} onChanged={me.refresh} /> : <StartShift onStarted={me.refresh} />}
      </>}
      <p className="driver-footnote">Herramienta temporal de prueba de GPS. Mantén esta página abierta y la pantalla encendida: el navegador no envía la ubicación en segundo plano. La app de conductores definitiva será Flutter.</p>
    </main>
  </div>
}

function StartShift({ onStarted }: { onStarted: () => void }) {
  const vehicles = useApiQuery(listMyVehicles, 'conductor-vehiculos', 30_000)
  const free = vehicles.data?.filter(v => v.libre) ?? []
  const [vehicle, setVehicle] = useState('')
  const selected = vehicle || (free.length === 1 ? free[0].vehiculoId : '')
  const action = useIdempotentAction(startMyShift, useCallback(() => onStarted(), [onStarted]))
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!action.locked && selected) action.submit({ vehiculoId: selected })
  }
  return <section className="driver-card" aria-labelledby="start-title">
    <h2 id="start-title">Iniciar turno</h2>
    {vehicles.error != null && <ErrorState message={describeFleetLoadError(vehicles.error)} onRetry={vehicles.refresh} busy={vehicles.refreshing} />}
    {vehicles.data && vehicles.data.length === 0 && <p className="driver-hint">No tienes vehículos autorizados. Pide al administrador que te autorice uno.</p>}
    {vehicles.data && vehicles.data.length > 0 && free.length === 0 && <p className="driver-hint">Tus vehículos autorizados están en otro turno ahora.</p>}
    {free.length > 0 && <form noValidate onSubmit={submit} aria-busy={action.sending}>
      <div className="field"><label htmlFor="mi-vehiculo">Vehículo</label>
        <select id="mi-vehiculo" value={action.state.status === 'uncertain' ? action.state.pending.input.vehiculoId : selected} disabled={action.locked}
          onChange={event => { setVehicle(event.target.value); if (action.state.status === 'rejected') action.reset() }}>
          {free.length !== 1 && <option value="">Elige tu vehículo</option>}
          {free.map(v => <option key={v.vehiculoId} value={v.vehiculoId}>{v.patente}</option>)}
        </select></div>
      {action.state.status !== 'uncertain' && <button type="submit" className="primary-button" disabled={action.sending || !selected}>{action.sending ? 'Iniciando…' : 'Iniciar turno'}</button>}
    </form>}
    <ActionNotice state={action.state} onRetry={action.retry} onDiscard={() => { action.reset(); onStarted() }} discardLabel="Descartar y comprobar" />
  </section>
}

function ShiftOpen({ me, onChanged }: { me: Me; onChanged: () => void }) {
  const { revalidate } = useAuth()
  const emitter = useGpsEmitter(me.intervaloEnvioSegundos, { onNoShift: onChanged, onSession: () => void revalidate() })
  const [closing, setClosing] = useState(false)
  const [reason, setReason] = useState('')
  const closeButton = useRef<HTMLButtonElement>(null)
  const stopEmitter = emitter.stop
  const close = useIdempotentAction(closeMyShift, useCallback(() => { stopEmitter(); onChanged() }, [stopEmitter, onChanged]))
  const fix = emitter.lastFix
  const serverState: GpsState = emitter.receipt?.estadoGps ?? me.estadoGps

  return <>
    <section className={emitter.sharing ? 'driver-card gps-panel is-sharing' : 'driver-card gps-panel'} aria-labelledby="gps-title">
      <div className="driver-head"><h2 id="gps-title">{emitter.sharing ? 'Compartiendo ubicación' : 'Ubicación detenida'}</h2><GpsBadge state={serverState} /></div>
      <p className="driver-hint">{emitter.sharing
        ? `Se envía una lectura real del GPS cada ${me.intervaloEnvioSegundos} s mientras el turno esté abierto.`
        : 'La central solo te verá en el mapa mientras compartas tu ubicación.'}</p>
      <button type="button" className={emitter.sharing ? 'secondary-button' : 'primary-button'} aria-pressed={emitter.sharing}
        onClick={emitter.sharing ? emitter.stop : emitter.start}>{emitter.sharing ? 'Detener envío' : 'Compartir mi ubicación'}</button>
      {emitter.issue && <div className="work-notice work-notice-warning" role="alert"><Icon name="alert" /><div className="work-notice-body"><p>{emitter.issue}</p></div></div>}
      <dl className="driver-facts" aria-live="polite">
        <div><dt>Última lectura</dt><dd>{fix ? <><time dateTime={fix.capturadaEn}>{new Date(fix.capturadaEn).toLocaleTimeString('es')}</time> · ±{Math.round(fix.precisionMetros)} m</> : 'Todavía no hay lectura'}</dd></div>
        <div><dt>Coordenadas</dt><dd className="tabular">{fix ? `${fix.latitud.toFixed(5)}, ${fix.longitud.toFixed(5)}` : '—'}</dd></div>
        <div><dt>Recibida por la central</dt><dd>{emitter.receipt
          ? <><time dateTime={emitter.receipt.recibidaEn}>{new Date(emitter.receipt.recibidaEn).toLocaleTimeString('es')}</time>{emitter.receipt.aceptada ? '' : ' · repetida'}</>
          : me.ultimaRecepcion ? <time dateTime={me.ultimaRecepcion}>{new Date(me.ultimaRecepcion).toLocaleTimeString('es')}</time> : 'Nada recibido en este turno'}</dd></div>
        <div><dt>Envíos aceptados</dt><dd className="tabular">{emitter.sentCount}</dd></div>
        {emitter.sharing && <div><dt>Pantalla</dt><dd>{emitter.screenLock === 'activo' ? 'Se mantiene encendida' : 'Tu navegador no permite mantenerla encendida'}</dd></div>}
      </dl>
    </section>
    <section className="driver-card" aria-labelledby="close-title">
      <h2 id="close-title">Terminar turno</h2>
      {!closing && <button ref={closeButton} type="button" className="secondary-button" onClick={() => setClosing(true)} disabled={me.estado === 'Ocupado'}>Cerrar turno</button>}
      {me.estado === 'Ocupado' && <p className="driver-hint">No puedes cerrar el turno con una oferta o un viaje activo.</p>}
      {closing && <form noValidate aria-busy={close.sending} onSubmit={event => { event.preventDefault(); if (!close.locked) close.submit({ motivo: reason.trim() || null }) }}>
        <div className="field"><label htmlFor="motivo-cierre">Motivo <span className="optional">(opcional)</span></label>
          <input id="motivo-cierre" maxLength={closeReasonMaxLength} placeholder="Fin de jornada" value={close.state.status === 'uncertain' ? close.state.pending.input.motivo ?? '' : reason}
            readOnly={close.locked} onChange={event => setReason(event.target.value)} /></div>
        {close.state.status !== 'uncertain' && <div className="driver-actions">
          <button type="submit" className="primary-button" disabled={close.sending}>{close.sending ? 'Cerrando…' : 'Confirmar cierre'}</button>
          <button type="button" className="text-action" disabled={close.sending} onClick={() => { setClosing(false); close.reset() }}>Cancelar</button>
        </div>}
      </form>}
      <ActionNotice state={close.state} onRetry={close.retry} onDiscard={() => { close.reset(); onChanged() }} discardLabel="Descartar y comprobar" />
    </section>
  </>
}
