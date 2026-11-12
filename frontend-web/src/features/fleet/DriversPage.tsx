import { useCallback, useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useApiQuery } from '../../shared/hooks/useApiQuery'
import { EmptyState, ErrorState, SkeletonRows, UpdatedAt } from '../../shared/components/feedback'
import { Icon } from '../../shared/components/Icon'
import { formatTime } from '../../shared/format'
import { ActionNotice } from './ActionNotice'
import { DriverAccessPanel } from './DriverAccessPanel'
import { useIsAdmin } from './use-is-admin'
import { useIdempotentAction } from './useIdempotentAction'
import { closeReasonMaxLength, closeShift, describeFleetLoadError, driverNameMaxLength, driverStateLabel, driverStates,
  listDrivers, listVehicles, openShift, registerDriver, validateDriverName } from './fleet'
import type { Driver, DriverState, Vehicle } from './fleet'

const stateHint: Record<DriverState, string> = {
  Disponible: 'Turno abierto, sin oferta ni viaje',
  Ocupado: 'Con oferta o viaje activo',
  FueraDeServicio: 'Sin turno abierto',
}

export function DriverStateBadge({ state }: { state: DriverState }) {
  return <span className={`state-badge driver-${state.toLowerCase()}`}>{driverStateLabel[state]}</span>
}

function parseFilter(value: string | null): DriverState | null {
  return driverStates.find(state => state === value) ?? null
}

export function DriversPage() {
  const isAdmin = useIsAdmin()
  const [params, setParams] = useSearchParams()
  const filter = parseFilter(params.get('estado'))
  const drivers = useApiQuery(listDrivers, 'conductores', 20_000)
  const vehicles = useApiQuery(listVehicles, 'vehiculos', 60_000)
  const [expanded, setExpanded] = useState<{ id: string; mode: 'turno' | 'acceso' } | null>(null)
  const [announcement, setAnnouncement] = useState<string | null>(null)
  const refreshDrivers = drivers.refresh
  const refreshVehicles = vehicles.refresh
  const refreshAll = useCallback(() => { refreshDrivers(); refreshVehicles() }, [refreshDrivers, refreshVehicles])

  const done = useCallback((message: string) => {
    setExpanded(null)
    setAnnouncement(message)
    refreshAll()
  }, [refreshAll])

  function choose(state: DriverState) {
    const next = new URLSearchParams()
    if (filter !== state) next.set('estado', state)
    setParams(next)
  }

  const data = drivers.data
  const counts = Object.fromEntries(driverStates.map(state => [state, data?.filter(d => d.estado === state).length ?? 0])) as Record<DriverState, number>
  const shown = data?.filter(d => !filter || d.estado === filter) ?? []
  const freeVehicles = vehicles.data?.filter(v => v.turnoId === null) ?? []

  return <div className="fleet-work">
    {isAdmin && <DriverRegisterForm onDone={nombre => { setAnnouncement(`Conductor registrado: ${nombre}.`); refreshDrivers() }} />}

    <section aria-labelledby="drivers-title">
      <div className="section-head">
        <h2 id="drivers-title">Conductores{data && <span className="count tabular"> · {data.length}</span>}</h2>
        <UpdatedAt updatedAt={drivers.updatedAt} refreshing={drivers.refreshing} stale={!!drivers.error} onRefresh={refreshAll} />
      </div>
      <div className="fleet-announce" role="status">{announcement && <div className="work-notice work-notice-success">
        <Icon name="check" /><div className="work-notice-body"><p>{announcement}</p></div></div>}</div>
      {drivers.error != null && <ErrorState message={data ? `${describeFleetLoadError(drivers.error)} Mostramos los últimos datos recibidos.` : describeFleetLoadError(drivers.error)} onRetry={refreshAll} busy={drivers.refreshing} />}
      {drivers.loading && <SkeletonRows rows={4} />}
      {data && data.length > 0 && <ol className="fleet-band" aria-label="Filtrar conductores por estado">{driverStates.map(state =>
        <li key={state} className={`band-${state.toLowerCase()}`}>
          <button type="button" aria-pressed={filter === state} onClick={() => choose(state)}>
            <span className="flow-count tabular">{counts[state]}</span>
            <span className="flow-label">{driverStateLabel[state]}</span>
            <span className="band-hint">{stateHint[state]}</span>
          </button>
        </li>)}</ol>}
      {data && data.length > 0 && <p className="gps-note"><Icon name="despacho" />
        <span>«Disponible» indica turno abierto, no conexión. La señal GPS real de cada conductor se ve en <Link to="/flota/ubicaciones">Ubicaciones</Link>.</span></p>}
      {data && data.length === 0 && <EmptyState title="Todavía no hay conductores">{isAdmin
        ? 'Registra el primer conductor con el formulario de arriba.' : 'Un administrador debe registrar los conductores de la empresa.'}</EmptyState>}
      {data && data.length > 0 && shown.length === 0 && <EmptyState title={`No hay conductores ${filter ? driverStateLabel[filter].toLowerCase() : ''}`}>
        <button type="button" className="text-action" onClick={() => setParams(new URLSearchParams())}>Ver todos</button></EmptyState>}
      {shown.length > 0 && <div className="fleet-table drivers-table" role="table" aria-label="Conductores">
        <div className="fleet-head" role="rowgroup"><div role="row" className="fleet-row">
          <span role="columnheader">Conductor</span><span role="columnheader">Estado</span><span role="columnheader">Vehículo</span>
          <span role="columnheader">Desde</span><span role="columnheader">Turno</span>
        </div></div>
        <div role="rowgroup">{shown.map(driver => <DriverRow key={driver.id} driver={driver} freeVehicles={freeVehicles}
          vehicles={vehicles.data ?? []} isAdmin={isAdmin}
          vehiclesReady={vehicles.data !== null} expanded={expanded?.id === driver.id ? expanded.mode : null}
          onToggle={mode => { setAnnouncement(null); setExpanded(current => current?.id === driver.id && current.mode === mode ? null : { id: driver.id, mode }) }}
          onDone={done} onUncertainDiscard={refreshAll} onAccessChanged={refreshDrivers} />)}</div>
      </div>}
    </section>
  </div>
}

function DriverRow({ driver, freeVehicles, vehicles, isAdmin, vehiclesReady, expanded, onToggle, onDone, onUncertainDiscard, onAccessChanged }: {
  driver: Driver; freeVehicles: Vehicle[]; vehicles: Vehicle[]; isAdmin: boolean; vehiclesReady: boolean; expanded: 'turno' | 'acceso' | null
  onToggle: (mode: 'turno' | 'acceso') => void; onDone: (message: string) => void; onUncertainDiscard: () => void; onAccessChanged: () => void
}) {
  const panelId = `turno-${driver.id}`
  const accessId = `acceso-panel-${driver.id}`
  const action = driver.estado === 'FueraDeServicio' ? 'Abrir turno' : driver.estado === 'Disponible' ? 'Cerrar turno' : null
  return <div role="row" className={expanded ? 'fleet-row is-open' : 'fleet-row'}>
    <span role="cell" className="fleet-name">{driver.nombre}
      {isAdmin && <button type="button" className="access-toggle" aria-expanded={expanded === 'acceso'} aria-controls={accessId}
        onClick={() => onToggle('acceso')}>{driver.accesoEmail ? 'Acceso y vehículos' : 'Sin acceso · configurar'}</button>}</span>
    <span role="cell" className="fleet-state"><DriverStateBadge state={driver.estado} />
      {driver.ocupacion && <span className="fleet-sub">{driver.ocupacion === 'Viaje' ? 'En viaje' : 'Oferta en curso'}</span>}</span>
    <span role="cell" className="fleet-plate">{driver.turno ? <span className="plate">{driver.turno.patente}</span> : <span className="fleet-none">Sin vehículo</span>}</span>
    <span role="cell" className="fleet-since tabular">{driver.turno ? <time dateTime={driver.turno.inicio}>{formatTime(driver.turno.inicio)}</time> : <span className="fleet-none">—</span>}</span>
    <span role="cell" className="fleet-action">
      {/* Abierto el formulario, solo él ofrece Cancelar: así no se pierde un resultado dudoso. */}
      {action && expanded !== 'turno' && <button type="button" className={action === 'Abrir turno' ? 'row-button row-button-primary' : 'row-button'}
        aria-expanded={false} aria-controls={panelId} onClick={() => onToggle('turno')}>{action}</button>}
      {!action && <><span className="fleet-none" aria-hidden="true">—</span>
        <span className="visually-hidden">No se puede cerrar el turno con una oferta o un viaje activo.</span></>}
    </span>
    {expanded === 'turno' && <div role="cell" className="fleet-panel" id={panelId}>
      {driver.estado === 'FueraDeServicio' && <OpenShiftForm driver={driver} freeVehicles={freeVehicles} vehiclesReady={vehiclesReady}
        onDone={onDone} onCancel={() => onToggle('turno')} onUncertainDiscard={onUncertainDiscard} />}
      {driver.estado === 'Disponible' && driver.turno && <CloseShiftForm driver={driver} turnoId={driver.turno.id}
        onDone={onDone} onCancel={() => onToggle('turno')} onUncertainDiscard={onUncertainDiscard} />}
    </div>}
    {expanded === 'acceso' && <div role="cell" className="fleet-panel" id={accessId}>
      <DriverAccessPanel driver={driver} vehicles={vehicles} onChanged={onAccessChanged} />
    </div>}
  </div>
}

function OpenShiftForm({ driver, freeVehicles, vehiclesReady, onDone, onCancel, onUncertainDiscard }: {
  driver: Driver; freeVehicles: Vehicle[]; vehiclesReady: boolean; onDone: (message: string) => void; onCancel: () => void; onUncertainDiscard: () => void
}) {
  const [vehicle, setVehicle] = useState(freeVehicles.length === 1 ? freeVehicles[0].id : '')
  const [error, setError] = useState<string | null>(null)
  const select = useRef<HTMLSelectElement>(null)
  // Placa elegida al enviar: tras confirmar, el vehículo deja de figurar entre los libres.
  const [chosen, setChosen] = useState<string | null>(null)
  const chosenPlate = useRef<string | null>(null)
  const action = useIdempotentAction(openShift, useCallback(() =>
    onDone(`Turno abierto: ${driver.nombre} con ${chosenPlate.current ?? 'el vehículo elegido'}.`), [driver.nombre, onDone]))
  useEffect(() => { select.current?.focus() }, [])
  const pendingVehicle = action.state.status === 'uncertain' || action.state.status === 'sending' ? action.state.pending.input.vehiculoId : vehicle

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (action.locked) return
    if (!vehicle) { setError('Elige el vehículo del turno.'); select.current?.focus(); return }
    chosenPlate.current = freeVehicles.find(v => v.id === vehicle)?.patente ?? null
    setChosen(chosenPlate.current)
    action.submit({ conductorId: driver.id, vehiculoId: vehicle })
  }

  if (vehiclesReady && freeVehicles.length === 0 && !action.locked) return <div className="work-notice" role="status">
    <Icon name="alert" /><div className="work-notice-body"><p>No hay vehículos libres para abrir el turno.</p>
      <Link className="text-action" to="/flota/vehiculos">Ir a vehículos</Link></div></div>

  return <form className="inline-form" noValidate onSubmit={submit} aria-busy={action.sending}>
    <div className="field"><label htmlFor={`vehiculo-${driver.id}`}>Vehículo para {driver.nombre}</label>
      <select ref={select} id={`vehiculo-${driver.id}`} value={pendingVehicle} disabled={action.locked || !vehiclesReady}
        aria-invalid={!!error} aria-describedby={error ? `vehiculo-error-${driver.id}` : undefined}
        onChange={event => { setVehicle(event.target.value); setError(null); if (action.state.status === 'rejected') action.reset() }}>
        {freeVehicles.length !== 1 && <option value="">{vehiclesReady ? 'Elige un vehículo libre' : 'Consultando vehículos…'}</option>}
        {freeVehicles.map(v => <option key={v.id} value={v.id}>{v.patente}</option>)}
        {action.locked && !freeVehicles.some(v => v.id === pendingVehicle) && <option value={pendingVehicle}>{chosen ?? 'Vehículo elegido'}</option>}
      </select>
      {error && <p className="field-error" id={`vehiculo-error-${driver.id}`}>{error}</p>}</div>
    {action.state.status !== 'uncertain' && <div className="inline-actions">
      <button type="submit" className="primary-button" disabled={action.sending}>{action.sending ? 'Abriendo…' : 'Abrir turno'}</button>
      <button type="button" className="text-action" onClick={onCancel} disabled={action.sending}>Cancelar</button>
    </div>}
    <ActionNotice state={action.state} onRetry={action.retry}
      onDiscard={() => { action.reset(); onUncertainDiscard() }} discardLabel="Descartar y comprobar la lista" />
  </form>
}

function CloseShiftForm({ driver, turnoId, onDone, onCancel, onUncertainDiscard }: {
  driver: Driver; turnoId: string; onDone: (message: string) => void; onCancel: () => void; onUncertainDiscard: () => void
}) {
  const [reason, setReason] = useState('')
  const input = useRef<HTMLInputElement>(null)
  const action = useIdempotentAction(closeShift, useCallback(() => onDone(`Turno cerrado: ${driver.nombre}.`), [driver.nombre, onDone]))
  useEffect(() => { input.current?.focus() }, [])
  const shownReason = action.state.status === 'uncertain' || action.state.status === 'sending' ? action.state.pending.input.motivo ?? '' : reason

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (action.locked) return
    action.submit({ turnoId, motivo: reason.trim() || null })
  }

  return <form className="inline-form" noValidate onSubmit={submit} aria-busy={action.sending}>
    <div className="field"><label htmlFor={`motivo-${driver.id}`}>Motivo del cierre <span className="optional">(opcional)</span></label>
      <input ref={input} id={`motivo-${driver.id}`} maxLength={closeReasonMaxLength} placeholder="Fin de jornada" autoComplete="off"
        value={shownReason} readOnly={action.locked} disabled={action.sending}
        onChange={event => { setReason(event.target.value); if (action.state.status === 'rejected') action.reset() }} /></div>
    {action.state.status !== 'uncertain' && <div className="inline-actions">
      <button type="submit" className="primary-button" disabled={action.sending}>{action.sending ? 'Cerrando…' : 'Cerrar turno'}</button>
      <button type="button" className="text-action" onClick={onCancel} disabled={action.sending}>Cancelar</button>
    </div>}
    <ActionNotice state={action.state} onRetry={action.retry}
      onDiscard={() => { action.reset(); onUncertainDiscard() }} discardLabel="Descartar y comprobar la lista" />
  </form>
}

function DriverRegisterForm({ onDone }: { onDone: (nombre: string) => void }) {
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)
  const input = useRef<HTMLInputElement>(null)
  const action = useIdempotentAction(registerDriver, useCallback((_r, value: { nombre: string }) => {
    setName(''); onDone(value.nombre); input.current?.focus()
  }, [onDone]))
  const shown = action.state.status === 'uncertain' || action.state.status === 'sending' ? action.state.pending.input.nombre : name

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (action.locked) return
    const problem = validateDriverName(name)
    setError(problem)
    if (problem) { input.current?.focus(); return }
    action.submit({ nombre: name.trim() })
  }

  return <section className="fleet-register" aria-labelledby="driver-register-title">
    <h2 id="driver-register-title">Registrar conductor</h2>
    <form className="inline-form" noValidate onSubmit={submit} aria-busy={action.sending}>
      <div className="field"><label htmlFor="conductor-nombre">Nombre completo</label>
        <input ref={input} id="conductor-nombre" autoComplete="off" maxLength={driverNameMaxLength} value={shown}
          readOnly={action.locked} disabled={action.sending} aria-invalid={!!error} aria-describedby={error ? 'conductor-nombre-error' : undefined}
          onChange={event => { setName(event.target.value); setError(null); if (action.state.status === 'rejected' || action.state.status === 'done') action.reset() }} />
        {error && <p className="field-error" id="conductor-nombre-error">{error}</p>}</div>
      {action.state.status !== 'uncertain' && <div className="inline-actions">
        <button type="submit" className="primary-button" disabled={action.sending}>{action.sending ? 'Registrando…' : 'Registrar conductor'}</button>
      </div>}
    </form>
    <ActionNotice state={action.state} onRetry={action.retry} onDiscard={() => { action.reset(); setName(''); input.current?.focus() }} />
  </section>
}
