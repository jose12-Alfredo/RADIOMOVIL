import { useEffect } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useApiQuery } from '../../shared/hooks/useApiQuery'
import { EmptyState, ErrorState, SkeletonRows, UpdatedAt } from '../../shared/components/feedback'
import { formatTime } from '../../shared/format'
import { Icon } from '../../shared/components/Icon'
import { StateBadge } from './StateBadge'
import { useConsole } from './console-context'
import { describeLoadError, listRequests, pageSize, requestStateLabel, requestStates } from './requests'
import type { RequestState, ServiceRequest } from './requests'

function parseState(value: string | null): RequestState | 'todas' {
  if (value === 'todas') return 'todas'
  return requestStates.find(state => state === value) ?? 'Pendiente'
}

function parsePage(value: string | null): number {
  const page = Number(value)
  return Number.isInteger(page) && page >= 1 ? page : 1
}

export function RequestsPage() {
  const [params, setParams] = useSearchParams()
  const { lastRegistered } = useConsole()
  const filter = parseState(params.get('estado'))
  const page = parsePage(params.get('pagina'))
  const query = useApiQuery(
    signal => listRequests({ estado: filter === 'todas' ? undefined : filter, page }, signal),
    `${filter}:${page}`, 15_000)

  const lastId = lastRegistered?.id
  const refresh = query.refresh
  useEffect(() => { if (lastId) refresh() }, [lastId, refresh])

  function go(next: { estado?: RequestState | 'todas'; pagina?: number }) {
    const updated = new URLSearchParams()
    const estado = next.estado ?? filter
    const pagina = next.pagina ?? 1
    if (estado !== 'Pendiente') updated.set('estado', estado)
    if (pagina > 1) updated.set('pagina', String(pagina))
    setParams(updated)
  }

  const data = query.data
  // La cola es FIFO: la solicitud nueva suele caer en otra página. Se fija arriba con los datos que
  // confirmó el servidor, sin cambiar de página, hasta que el operador registre la siguiente.
  const pinned = lastRegistered !== null && data !== null && !data.items.some(item => item.id === lastRegistered.id) &&
    (filter === 'todas' || filter === lastRegistered.estado) ? lastRegistered : null
  const totalPages = data ? Math.max(1, Math.ceil(data.total / pageSize)) : 1
  return <section className="queue" aria-labelledby="queue-title">
    <div className="section-head">
      <h2 id="queue-title">Cola de solicitudes{data && <span className="count tabular"> · {data.total}</span>}</h2>
      <UpdatedAt updatedAt={query.updatedAt} refreshing={query.refreshing} stale={!!query.error} onRefresh={query.refresh} />
    </div>
    <nav className="state-filter" aria-label="Filtrar por estado">
      {(['todas', ...requestStates] as const).map(state => <button key={state} type="button" aria-pressed={filter === state}
        onClick={() => go({ estado: state })}>{state === 'todas' ? 'Todas' : requestStateLabel[state]}</button>)}
    </nav>
    {query.error != null && <ErrorState message={data ? `${describeLoadError(query.error)} Mostramos los últimos datos recibidos.` : describeLoadError(query.error)} onRetry={query.refresh} busy={query.refreshing} />}
    {query.loading && <SkeletonRows />}
    {pinned && <div className="queue-pinned">
      <p>Recién registrada · ocupa su lugar por orden de recepción</p>
      <QueueTable items={[pinned]} highlighted={pinned.id} label="Solicitud recién registrada" />
    </div>}
    {data && data.items.length === 0 && !pinned && <EmptyState title={emptyTitle(filter, page)}>{filter === 'Pendiente' && page === 1 ? 'Las solicitudes registradas aparecerán aquí por orden de recepción.' : 'Prueba con otro estado.'}</EmptyState>}
    {data && data.items.length > 0 && <QueueTable items={data.items} highlighted={lastId ?? null} label="Solicitudes" />}
    {data && data.total > 0 && <div className="pagination">
      <button type="button" className="secondary-button" onClick={() => go({ pagina: page - 1 })} disabled={page <= 1}>Anterior</button>
      <span className="tabular" aria-live="polite">Página {page} de {totalPages}</span>
      <button type="button" className="secondary-button" onClick={() => go({ pagina: page + 1 })} disabled={page >= totalPages}>Siguiente</button>
    </div>}
  </section>
}

function emptyTitle(filter: RequestState | 'todas', page: number): string {
  if (page > 1) return 'No hay solicitudes en esta página'
  if (filter === 'todas') return 'Todavía no hay solicitudes'
  return `No hay solicitudes en estado ${requestStateLabel[filter].toLowerCase()}`
}

function QueueTable({ items, highlighted, label }: { items: ServiceRequest[]; highlighted: string | null; label: string }) {
  return <div className="queue-table" role="table" aria-label={label}>
    <div className="queue-head" role="rowgroup"><div role="row" className="queue-row">
      <span role="columnheader">Orden</span><span role="columnheader">Origen</span><span role="columnheader">Estado</span><span role="columnheader">Recibida</span><span role="columnheader" className="visually-hidden">Detalle</span>
    </div></div>
    <div role="rowgroup">{items.map(item => <div key={item.id} role="row" className={item.id === highlighted ? 'queue-row is-new' : 'queue-row'}>
      <span role="cell" className="queue-order tabular">{item.ordenRecepcion}</span>
      <span role="cell" className="queue-origin"><Link to={`/solicitudes/${item.id}`}>{item.origen}</Link></span>
      <span role="cell" className="queue-state"><StateBadge state={item.estado} /></span>
      <span role="cell" className="queue-time tabular"><time dateTime={item.creadaEn}>{formatTime(item.creadaEn)}</time></span>
      <span role="cell" className="queue-chevron"><Icon name="chevron" /></span>
    </div>)}</div>
  </div>
}
