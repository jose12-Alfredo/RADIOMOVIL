import { useSearchParams } from 'react-router-dom'
import { useApiQuery } from '../../shared/hooks/useApiQuery'
import { EmptyState, ErrorState, SkeletonRows, UpdatedAt } from '../../shared/components/feedback'
import { formatTime } from '../../shared/format'
import { describeFleetLoadError, listShifts, shiftsPageSize } from './fleet'

const filters = [
  { value: 'todos', label: 'Todos' },
  { value: 'abiertos', label: 'Abiertos' },
  { value: 'cerrados', label: 'Cerrados' },
] as const
type Filter = typeof filters[number]['value']

export function ShiftsPage() {
  const [params, setParams] = useSearchParams()
  const filter: Filter = filters.find(f => f.value === params.get('estado'))?.value ?? 'todos'
  const pageParam = Number(params.get('pagina'))
  const page = Number.isInteger(pageParam) && pageParam >= 1 ? pageParam : 1
  const abiertos = filter === 'todos' ? undefined : filter === 'abiertos'
  const query = useApiQuery(signal => listShifts({ abiertos, page }, signal), `turnos:${filter}:${page}`, 30_000)

  function go(next: { estado?: Filter; pagina?: number }) {
    const updated = new URLSearchParams()
    const estado = next.estado ?? filter
    if (estado !== 'todos') updated.set('estado', estado)
    if ((next.pagina ?? 1) > 1) updated.set('pagina', String(next.pagina))
    setParams(updated)
  }

  const data = query.data
  const totalPages = data ? Math.max(1, Math.ceil(data.total / shiftsPageSize)) : 1
  return <section className="fleet-work" aria-labelledby="shifts-title">
    <div className="section-head">
      <h2 id="shifts-title">Historial de turnos{data && <span className="count tabular"> · {data.total}</span>}</h2>
      <UpdatedAt updatedAt={query.updatedAt} refreshing={query.refreshing} stale={!!query.error} onRefresh={query.refresh} />
    </div>
    <nav className="state-filter" aria-label="Filtrar turnos">
      {filters.map(f => <button key={f.value} type="button" aria-pressed={filter === f.value} onClick={() => go({ estado: f.value })}>{f.label}</button>)}
    </nav>
    {query.error != null && <ErrorState message={data ? `${describeFleetLoadError(query.error)} Mostramos los últimos datos recibidos.` : describeFleetLoadError(query.error)} onRetry={query.refresh} busy={query.refreshing} />}
    {query.loading && <SkeletonRows />}
    {data && data.items.length === 0 && <EmptyState title={page > 1 ? 'No hay turnos en esta página' : filter === 'abiertos' ? 'No hay turnos abiertos' : 'Todavía no hay turnos'}>
      Los turnos se abren y cierran desde la lista de conductores.</EmptyState>}
    {data && data.items.length > 0 && <div className="fleet-table shifts-table" role="table" aria-label="Turnos">
      <div className="fleet-head" role="rowgroup"><div role="row" className="fleet-row">
        <span role="columnheader">Conductor</span><span role="columnheader">Vehículo</span><span role="columnheader">Inicio</span>
        <span role="columnheader">Fin</span><span role="columnheader">Motivo del cierre</span>
      </div></div>
      <div role="rowgroup">{data.items.map(shift => <div role="row" className="fleet-row" key={shift.id}>
        <span role="cell" className="fleet-name">{shift.conductorNombre}</span>
        <span role="cell"><span className="plate">{shift.patente}</span></span>
        <span role="cell" className="fleet-since tabular"><span className="cell-label">Inicio </span><time dateTime={shift.inicio}>{formatTime(shift.inicio)}</time></span>
        <span role="cell" className="fleet-since tabular">{shift.fin
          ? <><span className="cell-label">Fin </span><time dateTime={shift.fin}>{formatTime(shift.fin)}</time></>
          : <span className="state-badge driver-disponible">Abierto</span>}</span>
        <span role="cell" className={shift.fin ? 'fleet-reason' : 'fleet-reason is-open-shift'}>{shift.motivoCierre ?? <span className="fleet-none">{shift.fin ? 'Sin motivo' : '—'}</span>}</span>
      </div>)}</div>
    </div>}
    {data && totalPages > 1 && <div className="pagination">
      <button type="button" className="secondary-button" onClick={() => go({ pagina: page - 1 })} disabled={page <= 1}>Anterior</button>
      <span className="tabular" aria-live="polite">Página {page} de {totalPages}</span>
      <button type="button" className="secondary-button" onClick={() => go({ pagina: page + 1 })} disabled={page >= totalPages}>Siguiente</button>
    </div>}
  </section>
}
