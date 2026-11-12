import { Link } from 'react-router-dom'
import { useApiQuery } from '../../shared/hooks/useApiQuery'
import { EmptyState, ErrorState, SkeletonRows, UpdatedAt } from '../../shared/components/feedback'
import { Icon } from '../../shared/components/Icon'
import { formatTime } from '../../shared/format'
import { describeLoadError, listRequests } from '../requests/requests'
import { listActiveTrips, tripProgress, tripStateClass, tripStateLabel } from './dispatch'
import type { ActiveTrip } from './dispatch'

/** Despacho por asignación directa: qué falta asignar y cómo van las carreras ya asignadas. */
export function DispatchPage() {
  const pending = useApiQuery(signal => listRequests({ estado: 'Pendiente', pageSize: 50 }, signal), 'por-asignar', 10_000)
  const trips = useApiQuery(listActiveTrips, 'carreras', 10_000)
  return <div className="single-column">
    <section aria-labelledby="pending-title">
      <div className="section-head"><h2 id="pending-title">Por asignar{pending.data && <span className="count tabular"> · {pending.data.total}</span>}</h2>
        <UpdatedAt updatedAt={pending.updatedAt} refreshing={pending.refreshing} stale={!!pending.error} onRefresh={pending.refresh} /></div>
      {pending.error != null && <ErrorState message={describeLoadError(pending.error)} onRetry={pending.refresh} busy={pending.refreshing} />}
      {pending.loading && <SkeletonRows rows={2} />}
      {pending.data && pending.data.items.length === 0 && <EmptyState title="Nada por asignar">Las solicitudes nuevas aparecerán aquí, la más antigua primero.</EmptyState>}
      {pending.data && pending.data.items.length > 0 && <ul className="dispatch-list">{pending.data.items.map(item => <li key={item.id}>
        <Link to={`/solicitudes/${item.id}`} className="dispatch-row">
          <span className="dispatch-order tabular">{item.ordenRecepcion}</span>
          <span className="dispatch-main"><span className="dispatch-origin">{item.origen}</span>
            <span className="fleet-sub">{item.latitud === null ? 'Sin punto en el mapa' : 'Recogida marcada'} · recibida a las <time dateTime={item.creadaEn}>{formatTime(item.creadaEn)}</time></span></span>
          <span className="dispatch-go">Asignar<Icon name="chevron" /></span>
        </Link>
      </li>)}</ul>}
      {pending.data && pending.data.total > pending.data.items.length && <p className="fleet-sub">Se muestran las {pending.data.items.length} más antiguas. <Link className="text-action" to="/solicitudes?estado=Pendiente">Ver todas</Link></p>}
    </section>

    <section aria-labelledby="trips-title">
      <div className="section-head"><h2 id="trips-title">Carreras activas{trips.data && <span className="count tabular"> · {trips.data.length}</span>}</h2>
        <UpdatedAt updatedAt={trips.updatedAt} refreshing={trips.refreshing} stale={!!trips.error} onRefresh={trips.refresh} /></div>
      {trips.error != null && <ErrorState message={trips.data ? `${describeLoadError(trips.error)} La lista puede no estar al día.` : describeLoadError(trips.error)} onRetry={trips.refresh} busy={trips.refreshing} />}
      {trips.loading && <SkeletonRows rows={2} />}
      {trips.data && trips.data.length === 0 && <EmptyState title="No hay carreras activas">Al asignar una carrera aparecerá aquí con su conductor.</EmptyState>}
      {trips.data && trips.data.length > 0 && <ul className="dispatch-list">{trips.data.map(trip => <li key={trip.viajeId}>
        <Link to={`/solicitudes/${trip.solicitudId}`} className="dispatch-row">
          <span className="dispatch-order tabular">{trip.ordenRecepcion}</span>
          <span className="dispatch-main">
            <span className="dispatch-origin">{trip.origen}{trip.destino && <span className="fleet-sub"> → {trip.destino}</span>}</span>
            <span className="dispatch-driver"><span className="fleet-name">{trip.conductorNombre}</span>{trip.patente && <span className="plate">{trip.patente}</span>}</span>
          </span>
          <span className="dispatch-state">
            <span className={`state-badge ${tripStateClass[trip.estado]}`}>{tripStateLabel[trip.estado]}</span>
            <TripProgress trip={trip} />
          </span>
        </Link>
      </li>)}</ul>}
    </section>
  </div>
}

function TripProgress({ trip }: { trip: ActiveTrip }) {
  const progress = tripProgress(trip, formatTime)
  return <span className={progress.unseen ? 'fleet-sub trip-unseen' : 'fleet-sub'}>{progress.text}</span>
}
