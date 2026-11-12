import { useEffect, useRef, useState } from 'react'
import { createMap, fitPoints, maplibregl } from '../../shared/map/maplibre'
import { useApiQuery } from '../../shared/hooks/useApiQuery'
import { EmptyState, ErrorState, SkeletonRows, UpdatedAt } from '../../shared/components/feedback'
import { Icon } from '../../shared/components/Icon'
import { GpsBadge } from '../driver/DriverGpsPage'
import { describeFleetLoadError, formatAge, listLocations } from './fleet'
import type { DriverLocation } from './fleet'

/** Solo se dibujan posiciones con GPS reciente o desactualizado; "Sin señal" nunca aparece como ubicación actual. */
function onMap(item: DriverLocation): item is DriverLocation & { latitud: number; longitud: number; antiguedadSegundos: number } {
  return item.estadoGps !== 'SinSenal' && item.latitud !== null && item.longitud !== null && item.antiguedadSegundos !== null
}

export function LocationsPage() {
  const query = useApiQuery(listLocations, 'ubicaciones', 5_000)
  const data = query.data
  const items = data?.items ?? []
  const plotted = items.filter(onMap)
  const counts = { Reciente: items.filter(i => i.estadoGps === 'Reciente').length, Desactualizado: items.filter(i => i.estadoGps === 'Desactualizado').length,
    SinSenal: items.filter(i => i.estadoGps === 'SinSenal').length }
  return <div className="fleet-work locations">
    <div className="section-head">
      <h2 id="locations-title">Conductores en turno{data && <span className="count tabular"> · {items.length}</span>}</h2>
      <UpdatedAt updatedAt={query.updatedAt} refreshing={query.refreshing} stale={!!query.error} onRefresh={query.refresh} />
    </div>
    {query.error != null && <ErrorState message={data ? `${describeFleetLoadError(query.error)} Las posiciones mostradas pueden no ser actuales.` : describeFleetLoadError(query.error)} onRetry={query.refresh} busy={query.refreshing} />}
    {data && <p className="gps-note"><Icon name="despacho" /><span>
      Reciente: recibida hace {data.recienteSegundos} s o menos. Desactualizado: entre {data.recienteSegundos + 1} y {data.desactualizadoSegundos} s, se dibuja atenuado.
      Sin señal: más de {data.desactualizadoSegundos} s o ninguna, no se dibuja. Se actualiza cada 5 s.</span></p>}
    <div className="locations-grid">
      <div className="map-frame">
        <LiveMap items={plotted} />
        {data && plotted.length === 0 && <div className="map-empty" role="status">Ningún conductor en turno transmite ubicación ahora.</div>}
      </div>
      <section aria-labelledby="locations-title" className="locations-list">
        {query.loading && <SkeletonRows rows={3} />}
        {data && <ul className="gps-summary" aria-label="Resumen de señal">
          <li><span className="tabular">{counts.Reciente}</span> reciente</li>
          <li><span className="tabular">{counts.Desactualizado}</span> desactualizado</li>
          <li><span className="tabular">{counts.SinSenal}</span> sin señal</li>
        </ul>}
        {data && items.length === 0 && <EmptyState title="No hay conductores en turno">Aparecerán aquí al abrir su turno.</EmptyState>}
        {items.length > 0 && <ul className="location-rows">{items.map(item => <li key={item.conductorId} className={`location-row gps-row-${item.estadoGps.toLowerCase()}`}>
          <div className="location-main"><span className="fleet-name">{item.nombre}</span><span className="plate">{item.patente}</span></div>
          <div className="location-meta"><GpsBadge state={item.estadoGps} />
            <span className="fleet-sub tabular">{item.antiguedadSegundos === null ? 'Sin ubicaciones en este turno'
              : `${item.estadoGps === 'SinSenal' ? 'Última recepción ' : 'Recibida '}${formatAge(item.antiguedadSegundos)}${item.precisionMetros ? ` · ±${Math.round(item.precisionMetros)} m` : ''}`}</span></div>
        </li>)}</ul>}
      </section>
    </div>
  </div>
}

function LiveMap({ items }: { items: (DriverLocation & { latitud: number; longitud: number; antiguedadSegundos: number })[] }) {
  const container = useRef<HTMLDivElement>(null)
  const map = useRef<maplibregl.Map | null>(null)
  const markers = useRef(new Map<string, maplibregl.Marker>())
  const framed = useRef(false)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    if (!container.current) return
    let dispose = () => {}
    try {
      const created = createMap(container.current)
      map.current = created.map
      dispose = created.dispose
    } catch {
      // Sincronización con un sistema externo (WebGL): si no puede iniciarse, se informa en lugar de un hueco vacío.
      // oxlint-disable-next-line react/set-state-in-effect
      setFailed(true)
    }
    const current = markers.current
    return () => { current.forEach(m => m.remove()); current.clear(); dispose(); map.current = null }
  }, [])

  useEffect(() => {
    const instance = map.current
    if (!instance) return
    const seen = new Set<string>()
    for (const item of items) {
      seen.add(item.conductorId)
      const label = `${item.nombre}, ${item.patente}, ${item.estadoGps === 'Reciente' ? 'GPS reciente' : 'GPS desactualizado'}, ${formatAge(item.antiguedadSegundos)}`
      let marker = markers.current.get(item.conductorId)
      if (!marker) {
        const element = document.createElement('div')
        element.innerHTML = '<span class="map-dot"></span><span class="map-label"></span>'
        marker = new maplibregl.Marker({ element, anchor: 'left', offset: [-8, 0] }).setLngLat([item.longitud, item.latitud]).addTo(instance)
        markers.current.set(item.conductorId, marker)
      }
      const element = marker.getElement()
      element.className = `map-marker map-${item.estadoGps.toLowerCase()}`
      element.setAttribute('role', 'img')
      element.setAttribute('aria-label', label)
      element.querySelector('.map-label')!.textContent = item.estadoGps === 'Reciente' ? item.patente : `${item.patente} · ${formatAge(item.antiguedadSegundos)}`
      marker.setLngLat([item.longitud, item.latitud])
    }
    for (const [id, marker] of markers.current) if (!seen.has(id)) { marker.remove(); markers.current.delete(id) }
    // Encuadra una vez con las primeras posiciones reales; después respeta lo que haga la operadora.
    if (!framed.current && items.length > 0) {
      framed.current = true
      fitPoints(instance, items.map(i => [i.longitud, i.latitud]))
    }
  }, [items])

  if (failed) return <div className="map-failed" role="alert">Este navegador no pudo dibujar el mapa. La lista sigue mostrando las ubicaciones.</div>
  return <div ref={container} className="live-map" aria-label="Mapa de conductores en turno" role="region" />
}
