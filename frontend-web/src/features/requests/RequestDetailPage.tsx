import { Link, useParams } from 'react-router-dom'
import { useApiQuery } from '../../shared/hooks/useApiQuery'
import { ErrorState, SkeletonRows, UpdatedAt } from '../../shared/components/feedback'
import { formatTime } from '../../shared/format'
import { Icon } from '../../shared/components/Icon'
import { StateBadge } from './StateBadge'
import { describeLoadError, getRequest, isGuid } from './requests'
import { AssignPanel } from '../dispatch/AssignPanel'

// Notación GPS habitual (punto decimal, coma entre latitud y longitud), copiable a cualquier mapa.
const coords = (lat: number | null, lng: number | null) => `${(lat ?? 0).toFixed(5)}, ${(lng ?? 0).toFixed(5)}`

export function RequestDetailPage() {
  const { id = '' } = useParams()
  const valid = isGuid(id)
  const query = useApiQuery(signal => getRequest(id, signal), id, 15_000)
  const data = valid ? query.data : null
  return <div className="detail-page">
    <Link className="back-link" to="/solicitudes"><Icon name="back" />Volver a la cola</Link>
    {!valid && <ErrorState message="El enlace de esta solicitud no es válido." />}
    {valid && query.error != null && <ErrorState message={describeLoadError(query.error)} onRetry={query.refresh} busy={query.refreshing} />}
    {valid && query.loading && <SkeletonRows rows={3} />}
    {data && <>
      <header className="detail-head">
        <h2><span className="tabular">Orden {data.ordenRecepcion}</span> · {data.origen}</h2>
        <StateBadge state={data.estado} />
      </header>
      <dl className="detail-facts">
        <div><dt>Recibida</dt><dd><time dateTime={data.creadaEn}>{formatTime(data.creadaEn)}</time></dd></div>
        {data.nombrePasajero && <div><dt>Pasajero</dt><dd>{data.nombrePasajero}</dd></div>}
        {data.telefonoPasajero && <div><dt>Teléfono</dt><dd>{data.telefonoPasajero}</dd></div>}
        {data.destino && <div><dt>Destino</dt><dd>{data.destino}</dd></div>}
        {data.observaciones && <div><dt>Observaciones</dt><dd>{data.observaciones}</dd></div>}
        <div><dt>Recogida en el mapa</dt><dd>{data.latitud === null ? 'Sin marcar: el conductor solo verá la dirección' : <span className="tabular">Marcada · {coords(data.latitud, data.longitud)}</span>}</dd></div>
        {data.destinoLatitud !== null && <div><dt>Destino en el mapa</dt><dd className="tabular">Marcado · {coords(data.destinoLatitud, data.destinoLongitud)}</dd></div>}
        <div className="detail-secondary"><dt>Identificador</dt><dd className="tabular">{data.id}</dd></div>
      </dl>
      <UpdatedAt updatedAt={query.updatedAt} refreshing={query.refreshing} stale={!!query.error} onRefresh={query.refresh} />
      <AssignPanel request={data} onChanged={query.refresh} />
    </>}
  </div>
}
