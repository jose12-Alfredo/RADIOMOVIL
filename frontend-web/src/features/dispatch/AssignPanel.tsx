import { lazy, Suspense, useCallback, useMemo, useRef, useState } from 'react'
import { useApiQuery } from '../../shared/hooks/useApiQuery'
import { EmptyState, ErrorState, SkeletonRows, UpdatedAt } from '../../shared/components/feedback'
import { Icon } from '../../shared/components/Icon'
import { formatTime } from '../../shared/format'
import { GpsBadge } from '../driver/DriverGpsPage'
import { ActionNotice } from '../fleet/ActionNotice'
import { formatAge } from '../fleet/fleet'
import { useIdempotentAction } from '../fleet/useIdempotentAction'
import { describeLoadError } from '../requests/requests'
import type { ServiceRequest } from '../requests/requests'
import { assignTrip, formatDistance, listActiveTrips, listNearby, tripStateLabel } from './dispatch'
import type { NearbyCandidate } from './dispatch'
import type { LngLat } from './maps'

const AssignMap = lazy(() => import('./maps').then(m => ({ default: m.AssignMap })))

const point = (lat: number | null, lng: number | null): LngLat | null => lat === null || lng === null ? null : [lng, lat]

function distanceText(c: NearbyCandidate, hasPickup: boolean): string {
  if (c.estadoGps === 'SinSenal') return 'Sin GPS reciente: distancia desconocida'
  if (!hasPickup) return 'Sin punto de recogida: distancia no calculada'
  return c.distanciaMetros === null ? 'Distancia no disponible' : `A ${formatDistance(c.distanciaMetros)} en línea recta`
}

/**
 * Asignación DIRECTA: la operadora elige y el backend asigna. El conductor no acepta ni rechaza.
 * Nada se muestra como asignado hasta que el servidor lo confirma.
 */
export function AssignPanel({ request, onChanged }: { request: ServiceRequest; onChanged: () => void }) {
  // La confirmación sobrevive al cambio de estado: al pasar a Asignada el formulario se retira, el aviso no.
  const [assigned, setAssigned] = useState<NearbyCandidate | null>(null)
  if (request.estado === 'Pendiente') return <AssignForm request={request} onAssigned={candidate => { setAssigned(candidate); onChanged() }} />
  if (request.estado === 'Asignada' || request.estado === 'EnViaje') return <>
    <div role="status" className="fleet-announce">{assigned && <div className="work-notice work-notice-success assign-confirmed">
      <Icon name="check" /><div className="work-notice-body">
        <p><strong>Carrera asignada a {assigned.nombre} · {assigned.patente}</strong></p>
        <p>Confirmada por el servidor. La app del conductor mostrará la orden al recibirla.</p></div></div>}</div>
    <TripStatus solicitudId={request.id} />
  </>
  if (request.estado === 'Ofertada') return <section className="assign-section" aria-labelledby="assign-title">
    <h3 id="assign-title" className="assign-title">Asignación</h3>
    <div className="work-notice" role="status"><Icon name="alert" /><div className="work-notice-body">
      <p>Esta solicitud tiene una oferta del sistema anterior pendiente. No se puede asignar directamente hasta que esa oferta se resuelva.</p></div></div>
  </section>
  return null
}

function AssignForm({ request, onAssigned }: { request: ServiceRequest; onAssigned: (candidate: NearbyCandidate) => void }) {
  const query = useApiQuery(signal => listNearby(request.id, signal), `cercanos-${request.id}`, 5_000)
  const [choice, setChoice] = useState<string | null>(null)
  const sentTo = useRef<NearbyCandidate | null>(null)
  const run = useCallback((input: { solicitudId: string; conductorId: string }, key: string) => assignTrip(input, key), [])
  const action = useIdempotentAction(run, useCallback(() => { if (sentTo.current) onAssigned(sentTo.current) }, [onAssigned]))
  const items = query.data?.items ?? []
  // Si el conductor deja de estar disponible, la selección desaparece con él.
  const selected = items.find(c => c.conductorId === choice) ?? null
  // Referencias estables: el mapa solo recoloca recogida/destino si cambian de verdad.
  const pickup = useMemo(() => point(request.latitud, request.longitud), [request.latitud, request.longitud])
  const destination = useMemo(() => point(request.destinoLatitud, request.destinoLongitud), [request.destinoLatitud, request.destinoLongitud])
  const withPosition = items.filter(c => c.estadoGps !== 'SinSenal').length

  function assign() {
    if (!selected || action.locked) return
    sentTo.current = selected
    action.submit({ solicitudId: request.id, conductorId: selected.conductorId })
  }

  return <section className="assign-section" aria-labelledby="assign-title">
    <div className="section-head">
      <h3 id="assign-title" className="assign-title">Asignar carrera{query.data && <span className="count tabular"> · {items.length} disponibles</span>}</h3>
      <UpdatedAt updatedAt={query.updatedAt} refreshing={query.refreshing} stale={!!query.error} onRefresh={query.refresh} />
    </div>
    <p className="gps-note"><Icon name="despacho" /><span>
      {pickup ? 'Ordenados por distancia en línea recta desde su GPS real hasta la recogida; no es ruta ni tiempo de llegada.'
        : 'Esta solicitud no tiene punto en el mapa: no se puede ordenar por cercanía y el conductor solo verá la dirección escrita.'}
      {' '}El conductor recibe la orden sin aceptarla ni rechazarla.</span></p>
    {query.error != null && <ErrorState message={query.data ? `${describeLoadError(query.error)} La lista puede no estar al día.` : describeLoadError(query.error)} onRetry={query.refresh} busy={query.refreshing} />}
    <div className="assign-grid">
      {(pickup || withPosition > 0) && <div>
        <div className="map-frame assign-map">
          <Suspense fallback={<div className="picker-loading" role="status">Cargando mapa…</div>}>
            <AssignMap pickup={pickup} destination={destination} candidates={items} selectedId={selected?.conductorId ?? null} onSelect={id => { if (!action.locked) setChoice(id) }} />
          </Suspense>
        </div>
        <ul className="map-legend" aria-label="Leyenda del mapa">
          {pickup && <li><span className="map-key map-key-recogida" aria-hidden="true" />Recogida</li>}
          {destination && <li><span className="map-key map-key-destino" aria-hidden="true" />Destino</li>}
          <li><span className="map-key map-key-driver" aria-hidden="true" />GPS reciente</li>
          <li><span className="map-key map-key-stale" aria-hidden="true" />GPS desactualizado</li>
        </ul>
      </div>}
      <div className="assign-list">
        {query.loading && <SkeletonRows rows={3} />}
        {query.data && items.length === 0 && <EmptyState title="No hay conductores disponibles">Aparecerán aquí al abrir turno y quedar libres.</EmptyState>}
        {items.length > 0 && <fieldset className="assign-options" disabled={action.locked}>
          <legend className="visually-hidden">Conductor para esta carrera</legend>
          {items.map(c => <label key={c.conductorId} className={`assign-option gps-row-${c.estadoGps.toLowerCase()}`}>
            <input type="radio" name="conductor" value={c.conductorId} checked={selected?.conductorId === c.conductorId} onChange={() => setChoice(c.conductorId)} />
            <span className="assign-option-body">
              <span className="assign-option-main"><span className="fleet-name">{c.nombre}</span><span className="plate">{c.patente}</span></span>
              <span className="assign-option-meta"><GpsBadge state={c.estadoGps} />
                <span className="fleet-sub tabular">{distanceText(c, !!pickup)}{c.antiguedadSegundos !== null && c.estadoGps !== 'SinSenal' ? ` · GPS ${formatAge(c.antiguedadSegundos)}` : ''}</span></span>
            </span>
          </label>)}
        </fieldset>}
        {selected && selected.estadoGps !== 'Reciente' && action.state.status !== 'done' && <p className="field-help assign-caution">
          {selected.estadoGps === 'SinSenal' ? 'No hay posición reciente de este conductor.' : 'Su última posición no es reciente.'} Confírmalo por radio si dudas de dónde está.</p>}
        <ActionNotice state={action.state} onRetry={action.retry} onDiscard={action.reset} />
        <button type="button" className="primary-button assign-button" disabled={!selected || action.locked || action.state.status === 'done'} onClick={assign}>
          {action.sending ? 'Asignando…' : selected ? `Asignar carrera a ${selected.nombre} · ${selected.patente}` : 'Elige un conductor para asignar'}</button>
      </div>
    </div>
  </section>
}

/** Estado de la carrera ya asignada: quién la tiene y si la app del conductor ya mostró la orden. */
function TripStatus({ solicitudId }: { solicitudId: string }) {
  const query = useApiQuery(listActiveTrips, 'carreras', 10_000)
  const trip = query.data?.find(t => t.solicitudId === solicitudId)
  return <section className="assign-section" aria-labelledby="trip-title">
    <div className="section-head"><h3 id="trip-title" className="assign-title">Carrera</h3>
      <UpdatedAt updatedAt={query.updatedAt} refreshing={query.refreshing} stale={!!query.error} onRefresh={query.refresh} /></div>
    {query.error != null && <ErrorState message={describeLoadError(query.error)} onRetry={query.refresh} busy={query.refreshing} />}
    {query.loading && <SkeletonRows rows={2} />}
    {query.data && !trip && <p className="fleet-sub">La carrera ya no está activa.</p>}
    {trip && <dl className="detail-facts">
      <div><dt>Conductor</dt><dd><span className="fleet-name">{trip.conductorNombre}</span>{trip.patente && <> <span className="plate">{trip.patente}</span></>}</dd></div>
      <div><dt>Estado</dt><dd>{tripStateLabel[trip.estado]}{trip.ultimoPasoEn && trip.estado !== 'Asignado' && <> desde las <time dateTime={trip.ultimoPasoEn}>{formatTime(trip.ultimoPasoEn)}</time></>}
        <span className="fleet-sub"> · asignada a las <time dateTime={trip.asignadoEn}>{formatTime(trip.asignadoEn)}</time></span></dd></div>
      <div><dt>Orden en la app</dt><dd>{trip.recibidaEn ? <>Vista por el conductor a las <time dateTime={trip.recibidaEn}>{formatTime(trip.recibidaEn)}</time></>
        : <span className="trip-unseen">Aún no confirmada por la app del conductor</span>}</dd></div>
    </dl>}
  </section>
}
