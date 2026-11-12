import { useCallback, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useApiQuery } from '../../shared/hooks/useApiQuery'
import { EmptyState, ErrorState, SkeletonRows, UpdatedAt } from '../../shared/components/feedback'
import { Icon } from '../../shared/components/Icon'
import { ActionNotice } from './ActionNotice'
import { useIsAdmin } from './use-is-admin'
import { useIdempotentAction } from './useIdempotentAction'
import { describeFleetLoadError, listVehicles, normalizePlate, registerVehicle, validatePlate } from './fleet'

export function VehiclesPage() {
  const isAdmin = useIsAdmin()
  const vehicles = useApiQuery(listVehicles, 'vehiculos', 30_000)
  const [announcement, setAnnouncement] = useState<string | null>(null)
  const refresh = vehicles.refresh
  const data = vehicles.data
  const inShift = data?.filter(v => v.turnoId !== null).length ?? 0
  return <div className="fleet-work">
    {isAdmin && <VehicleRegisterForm onDone={plate => { setAnnouncement(`Vehículo registrado: ${plate}.`); refresh() }} />}
    <section aria-labelledby="vehicles-title">
      <div className="section-head">
        <h2 id="vehicles-title">Vehículos{data && <span className="count tabular"> · {data.length} · {inShift} en turno</span>}</h2>
        <UpdatedAt updatedAt={vehicles.updatedAt} refreshing={vehicles.refreshing} stale={!!vehicles.error} onRefresh={refresh} />
      </div>
      <div className="fleet-announce" role="status">{announcement && <div className="work-notice work-notice-success">
        <Icon name="check" /><div className="work-notice-body"><p>{announcement}</p></div></div>}</div>
      {vehicles.error != null && <ErrorState message={data ? `${describeFleetLoadError(vehicles.error)} Mostramos los últimos datos recibidos.` : describeFleetLoadError(vehicles.error)} onRetry={refresh} busy={vehicles.refreshing} />}
      {vehicles.loading && <SkeletonRows rows={3} />}
      {data && data.length === 0 && <EmptyState title="Todavía no hay vehículos">{isAdmin
        ? 'Registra el primer vehículo con su placa.' : 'Un administrador debe registrar los vehículos de la empresa.'}</EmptyState>}
      {data && data.length > 0 && <div className="fleet-table vehicles-table" role="table" aria-label="Vehículos">
        <div className="fleet-head" role="rowgroup"><div role="row" className="fleet-row">
          <span role="columnheader">Placa</span><span role="columnheader">Situación</span><span role="columnheader">Conductor en turno</span>
        </div></div>
        <div role="rowgroup">{data.map(vehicle => <div role="row" className="fleet-row" key={vehicle.id}>
          <span role="cell"><span className="plate">{vehicle.patente}</span></span>
          <span role="cell">{vehicle.turnoId
            ? <span className="state-badge vehicle-inshift">En turno</span>
            : <span className="state-badge vehicle-free">Libre</span>}</span>
          <span role="cell" className="fleet-name">{vehicle.conductorNombre ?? <span className="fleet-none">—</span>}</span>
        </div>)}</div>
      </div>}
    </section>
  </div>
}

function VehicleRegisterForm({ onDone }: { onDone: (plate: string) => void }) {
  const [plate, setPlate] = useState('')
  const [error, setError] = useState<string | null>(null)
  const input = useRef<HTMLInputElement>(null)
  const action = useIdempotentAction(registerVehicle, useCallback((_r, value: { patente: string }) => {
    setPlate(''); onDone(value.patente); input.current?.focus()
  }, [onDone]))
  const shown = action.state.status === 'uncertain' || action.state.status === 'sending' ? action.state.pending.input.patente : plate

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (action.locked) return
    const problem = validatePlate(plate)
    setError(problem)
    if (problem) { input.current?.focus(); return }
    action.submit({ patente: normalizePlate(plate) })
  }

  return <section className="fleet-register" aria-labelledby="vehicle-register-title">
    <h2 id="vehicle-register-title">Registrar vehículo</h2>
    <form className="inline-form" noValidate onSubmit={submit} aria-busy={action.sending}>
      <div className="field"><label htmlFor="vehiculo-placa">Placa</label>
        <input ref={input} id="vehiculo-placa" className="plate-input" autoComplete="off" maxLength={24} value={shown}
          placeholder="Ej. 2345-ABC" spellCheck={false} autoCapitalize="characters"
          readOnly={action.locked} disabled={action.sending} aria-invalid={!!error}
          aria-describedby={error ? 'vehiculo-placa-error' : 'vehiculo-placa-ayuda'}
          onChange={event => { setPlate(event.target.value); setError(null); if (action.state.status === 'rejected' || action.state.status === 'done') action.reset() }} />
        {error ? <p className="field-error" id="vehiculo-placa-error">{error}</p>
          : <p className="field-help" id="vehiculo-placa-ayuda">Se guarda en mayúsculas y sin espacios.</p>}</div>
      {action.state.status !== 'uncertain' && <div className="inline-actions">
        <button type="submit" className="primary-button" disabled={action.sending}>{action.sending ? 'Registrando…' : 'Registrar vehículo'}</button>
      </div>}
    </form>
    <ActionNotice state={action.state} onRetry={action.retry} onDiscard={() => { action.reset(); setPlate(''); input.current?.focus() }} />
  </section>
}
