import { useEffect, useRef, useState } from 'react'
import { createMap, fitPoints, labelledMarker, maplibregl } from '../../shared/map/maplibre'
import { listLocations } from '../fleet/fleet'
import { loadInitialView } from '../../shared/map/initialView'
import type { MapView } from '../../shared/map/initialView'
import { formatDistance } from './dispatch'
import type { NearbyCandidate } from './dispatch'

// Módulo diferido: MapLibre solo se descarga al abrir un mapa de la consola.

export type LngLat = [number, number]
export type PickTarget = 'recogida' | 'destino'

function useMap(onFail: () => void) {
  const container = useRef<HTMLDivElement>(null)
  const [map, setMap] = useState<maplibregl.Map | null>(null)
  const fail = useRef(onFail)
  useEffect(() => { fail.current = onFail })
  useEffect(() => {
    if (!container.current) return
    let dispose = () => {}
    try {
      const created = createMap(container.current)
      dispose = created.dispose
      // Sincronización con un sistema externo (instancia WebGL creada aquí).
      // oxlint-disable-next-line react/set-state-in-effect
      setMap(created.map)
    } catch { fail.current() }
    return () => { dispose(); setMap(null) }
  }, [])
  return { container, map }
}

function MapFailed() {
  return <div className="map-failed" role="alert">Este navegador no pudo dibujar el mapa. La solicitud puede registrarse y asignarse sin él.</div>
}

export type PickerControls = { getView: () => MapView | null }

/**
 * Mapa grande del registro. Tocar el mapa coloca SOLO el punto que se está editando; cada marcador se ajusta
 * arrastrándolo (cambia únicamente ese punto) y tocarlo lo elige para editar. Sin ratón: las flechas desplazan el
 * mapa y «Marcar el centro» usa la mira. Nunca se convierten direcciones en coordenadas.
 */
export function PointPickerMap({ pickup, destination, target, readOnly, onPick, onSelectTarget, focus, onControls }: {
  pickup: LngLat | null; destination: LngLat | null; target: PickTarget; readOnly: boolean
  onPick: (target: PickTarget, point: LngLat) => void; onSelectTarget: (target: PickTarget) => void
  focus: { point: LngLat; n: number } | null; onControls?: (controls: PickerControls | null) => void
}) {
  const [failed, setFailed] = useState(false)
  const { container, map } = useMap(() => setFailed(true))
  const markers = useRef<{ recogida?: maplibregl.Marker; destino?: maplibregl.Marker }>({})
  const live = useRef({ target, readOnly, onPick, onSelectTarget })
  useEffect(() => { live.current = { target, readOnly, onPick, onSelectTarget } })

  useEffect(() => {
    if (!onControls || !map) return
    onControls({ getView: () => { const c = map.getCenter(); return { lat: c.lat, lng: c.lng, zoom: map.getZoom() } } })
    return () => onControls(null)
  }, [map, onControls])

  // Vista inicial: puntos ya marcados → vista guardada en este navegador → posiciones REALES recientes de la flota.
  const framed = useRef(false)
  useEffect(() => {
    if (!map || framed.current) return
    framed.current = true
    const marked = [pickup, destination].filter((p): p is LngLat => p !== null)
    if (marked.length) { fitPoints(map, marked); return }
    const saved = loadInitialView()
    if (saved) { map.resize(); map.jumpTo({ center: [saved.lng, saved.lat], zoom: saved.zoom }); return }
    const controller = new AbortController()
    listLocations(controller.signal).then(snapshot => {
      const fleet = snapshot.items.filter(i => i.estadoGps !== 'SinSenal' && i.latitud !== null && i.longitud !== null)
        .map(i => [i.longitud, i.latitud] as LngLat)
      if (fleet.length) fitPoints(map, fleet, 14)
    }).catch(() => undefined)
    return () => controller.abort()
  }, [map, pickup, destination])

  useEffect(() => {
    if (!map || !focus) return
    map.easeTo({ center: focus.point, zoom: Math.max(map.getZoom(), 16), duration: 400 })
  }, [map, focus])

  useEffect(() => {
    if (!map) return
    const click = (event: maplibregl.MapMouseEvent) => {
      if (live.current.readOnly) return
      live.current.onPick(live.current.target, [event.lngLat.lng, event.lngLat.lat])
    }
    map.on('click', click)
    return () => { map.off('click', click) }
  }, [map])

  useEffect(() => {
    if (!map) return
    for (const [key, point, label] of [['recogida', pickup, 'Recogida'], ['destino', destination, 'Destino']] as const) {
      let marker = markers.current[key]
      if (!point) { marker?.remove(); delete markers.current[key]; continue }
      if (!marker) {
        marker = labelledMarker(`map-${key} map-pin`, label, label, 'left').setLngLat(point).addTo(map)
        const element = marker.getElement()
        element.addEventListener('click', event => { event.stopPropagation(); if (!live.current.readOnly) live.current.onSelectTarget(key) })
        marker.on('dragstart', () => live.current.onSelectTarget(key))
        marker.on('dragend', () => { const p = marker!.getLngLat(); live.current.onPick(key, [p.lng, p.lat]) })
        markers.current[key] = marker
      }
      marker.setLngLat(point).setDraggable(!readOnly)
      const element = marker.getElement()
      element.classList.toggle('is-active', !readOnly && key === target)
      element.setAttribute('aria-label', readOnly ? `${label} marcada` : `${label} marcada. Arrástrala para ajustarla`)
    }
  }, [map, pickup, destination, target, readOnly])

  useEffect(() => {
    const current = markers.current
    return () => { current.recogida?.remove(); current.destino?.remove() }
  }, [])

  if (failed) return <MapFailed />
  return <div className={`picker-map${readOnly ? ' is-readonly' : ''}`}>
    <div ref={container} className="live-map" role="region" aria-label={readOnly ? 'Mapa de la solicitud' : `Mapa: tocar marca ${target === 'recogida' ? 'la recogida' : 'el destino'}`} />
    {!readOnly && <span className="picker-crosshair" aria-hidden="true" />}
    {!readOnly && <button type="button" className="picker-center secondary-button" disabled={!map}
      onClick={() => { const c = map?.getCenter(); if (c) onPick(target, [c.lng, c.lat]) }}>Marcar el centro</button>}
  </div>
}

/** Recogida y conductores disponibles; tocar un conductor lo selecciona. «Sin señal» no se dibuja. */
export function AssignMap({ pickup, destination, candidates, selectedId, onSelect }:
  { pickup: LngLat | null; destination: LngLat | null; candidates: NearbyCandidate[]; selectedId: string | null; onSelect: (id: string) => void }) {
  const [failed, setFailed] = useState(false)
  const { container, map } = useMap(() => setFailed(true))
  const fixed = useRef<maplibregl.Marker[]>([])
  const drivers = useRef(new Map<string, maplibregl.Marker>())
  const select = useRef(onSelect)
  useEffect(() => { select.current = onSelect })
  const framed = useRef(false)

  useEffect(() => {
    if (!map) return
    fixed.current.forEach(m => m.remove())
    fixed.current = []
    // Símbolos sin texto (leyenda bajo el mapa): las placas de los conductores cercanos nunca quedan tapadas.
    if (pickup) fixed.current.push(labelledMarker('map-recogida map-symbol', '', 'Punto de recogida', 'left').setLngLat(pickup).addTo(map))
    if (destination) fixed.current.push(labelledMarker('map-destino map-symbol', '', 'Destino', 'left').setLngLat(destination).addTo(map))
  }, [map, pickup, destination])

  useEffect(() => {
    if (!map) return
    const visible = candidates.filter(c => c.estadoGps !== 'SinSenal' && c.latitud !== null && c.longitud !== null)
    const seen = new Set<string>()
    for (const c of visible) {
      seen.add(c.conductorId)
      const point: LngLat = [c.longitud!, c.latitud!]
      const distance = c.distanciaMetros === null ? '' : ` a ${formatDistance(c.distanciaMetros)}`
      let marker = drivers.current.get(c.conductorId)
      if (!marker) {
        marker = labelledMarker('', c.patente, '').setLngLat(point).addTo(map)
        const element = marker.getElement()
        element.setAttribute('role', 'button')
        element.tabIndex = 0
        const choose = () => select.current(c.conductorId)
        element.addEventListener('click', event => { event.stopPropagation(); choose() })
        element.addEventListener('keydown', event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); choose() } })
        drivers.current.set(c.conductorId, marker)
      }
      marker.setLngLat(point)
      const element = marker.getElement()
      element.className = `map-marker map-${c.estadoGps.toLowerCase()}${c.conductorId === selectedId ? ' is-selected' : ''}`
      element.setAttribute('aria-label', `${c.nombre}, ${c.patente}${distance}. ${c.conductorId === selectedId ? 'Seleccionado' : 'Seleccionar'}`)
      element.setAttribute('aria-pressed', String(c.conductorId === selectedId))
    }
    for (const [id, marker] of drivers.current) if (!seen.has(id)) { marker.remove(); drivers.current.delete(id) }
    if (!framed.current && (pickup || visible.length)) {
      framed.current = true
      fitPoints(map, [...(pickup ? [pickup] : []), ...visible.map(c => [c.longitud!, c.latitud!] as LngLat)])
    }
  }, [map, candidates, selectedId, pickup])

  useEffect(() => {
    const current = drivers.current
    return () => { current.forEach(m => m.remove()); current.clear() }
  }, [])

  if (failed) return <MapFailed />
  return <div ref={container} className="live-map" role="region" aria-label="Mapa de recogida y conductores disponibles" />
}
