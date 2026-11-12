import { useEffect } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useApiQuery } from '../../shared/hooks/useApiQuery'
import { EmptyState, ErrorState, SkeletonRows, UpdatedAt } from '../../shared/components/feedback'
import { formatTime } from '../../shared/format'
import { StateBadge } from '../requests/StateBadge'
import { useConsole } from '../requests/console-context'
import { countByState, describeLoadError, listCandidates, listRequests, requestStateLabel, requestStates } from '../requests/requests'

async function loadDashboard(signal: AbortSignal) {
  const [counts, next, candidates] = await Promise.all([
    countByState(signal), listRequests({ estado: 'Pendiente', page: 1, pageSize: 5 }, signal), listCandidates(signal),
  ])
  return { counts, next, candidates }
}

export function DashboardPage() {
  const navigate = useNavigate()
  const { lastRegistered } = useConsole()
  const query = useApiQuery(loadDashboard, 'panel', 20_000)
  const lastId = lastRegistered?.id
  const refresh = query.refresh
  useEffect(() => { if (lastId) refresh() }, [lastId, refresh])
  const data = query.data
  return <div className="panel-work">
    <section aria-labelledby="flow-title">
      <div className="section-head"><h2 id="flow-title">Solicitudes por estado</h2>
        <UpdatedAt updatedAt={query.updatedAt} refreshing={query.refreshing} stale={!!query.error} onRefresh={query.refresh} /></div>
      {query.error != null && <ErrorState message={data ? `${describeLoadError(query.error)} Mostramos los últimos datos recibidos.` : describeLoadError(query.error)} onRetry={query.refresh} busy={query.refreshing} />}
      {query.loading && <SkeletonRows rows={2} />}
      {data && <ol className="state-flow">{requestStates.map(state =>
        <li key={state} className={`flow-${state.toLowerCase()}`}><Link to={state === 'Pendiente' ? '/solicitudes' : `/solicitudes?estado=${state}`}>
          <span className="flow-count tabular">{data.counts[state]}</span>
          <span className="flow-label">{requestStateLabel[state]}</span>
        </Link></li>)}</ol>}
    </section>
    <section aria-labelledby="next-title">
      <div className="section-head"><h2 id="next-title">Siguientes por atender</h2>
        {data && data.next.total > 0 && <Link className="text-action" to="/solicitudes">Ver cola completa</Link>}</div>
      {query.loading && <SkeletonRows rows={3} />}
      {data && data.next.items.length === 0 && <EmptyState title="No hay solicitudes pendientes">Las nuevas llamadas registradas aparecerán aquí por orden de recepción.</EmptyState>}
      {data && data.next.items.length > 0 && <ul className="next-list">{data.next.items.map(item => <li key={item.id}>
        <button type="button" onClick={() => navigate(`/solicitudes/${item.id}`)}>
          <span className="queue-order tabular">{item.ordenRecepcion}</span>
          <span className="next-origin">{item.origen}</span>
          <span className="queue-time tabular"><time dateTime={item.creadaEn}>{formatTime(item.creadaEn)}</time></span>
          <StateBadge state={item.estado} />
        </button></li>)}</ul>}
    </section>
    <section aria-labelledby="drivers-title" className="panel-drivers">
      <div className="section-head"><h2 id="drivers-title">Conductores candidatos</h2>
        <Link className="text-action" to="/despacho">Ver despacho</Link></div>
      {query.loading && <SkeletonRows rows={1} />}
      {data && <p className="drivers-line"><span className="flow-count tabular">{data.candidates.length}</span>
        <span>{data.candidates.length === 0 ? 'Sin conductores con turno activo y libres.' : data.candidates.length === 1 ? 'Con turno activo y libre. Ubicación aún sin verificar.' : 'Con turno activo y libres. Ubicación aún sin verificar.'}</span></p>}
    </section>
  </div>
}
