import * as maplibregl from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import workerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?url'

// MapLibre 6 carga su worker como módulo aparte: Vite lo emite como asset y aquí se indica su URL.
// Este módulo solo se importa desde rutas/componentes diferidos (MapLibre pesa ~1 MB).
maplibregl.setWorkerUrl(workerUrl)

export { maplibregl }

// Teselas de OpenStreetMap SOLO para pruebas de bajo volumen (con atribución). Proveedor de producción: pendiente.
export const testStyle: maplibregl.StyleSpecification = {
  version: 8,
  sources: { osm: { type: 'raster', tiles: ['https://tile.openstreetmap.org/{z}/{x}/{y}.png'], tileSize: 256, maxzoom: 19,
    attribution: '© <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>' } },
  layers: [{ id: 'osm', type: 'raster', source: 'osm' }],
}

/**
 * Crea el mapa y lo mantiene del tamaño de su contenedor. maplibre-gl.css (sin capa) impone position:relative
 * al contenedor: el tamaño se fija con width/height en CSS, nunca con inset.
 */
export function createMap(container: HTMLElement, options: Partial<maplibregl.MapOptions> = {}) {
  const map = new maplibregl.Map({ container, style: testStyle, center: [0, 0], zoom: 1, attributionControl: { compact: true }, ...options })
  map.addControl(new maplibregl.NavigationControl({ showCompass: false }), 'top-right')
  const observer = new ResizeObserver(() => map.resize())
  observer.observe(container)
  return { map, dispose: () => { observer.disconnect(); map.remove() } }
}

/** Encuadre con margen derecho para la etiqueta (placa · dato) que se dibuja a la derecha de cada punto. */
export const labelPadding = { top: 56, bottom: 56, left: 112, right: 168 }

export function fitPoints(map: maplibregl.Map, points: [number, number][], maxZoom = 16) {
  if (points.length === 0) return
  map.resize()
  if (points.length === 1) { map.jumpTo({ center: points[0], zoom: Math.min(maxZoom, 15) }); return }
  const bounds = new maplibregl.LngLatBounds()
  points.forEach(p => bounds.extend(p))
  map.fitBounds(bounds, { padding: labelPadding, maxZoom, duration: 0 })
}

/**
 * Marcador accesible con punto y etiqueta; el estilo lo da la clase. Los puntos fijos (recogida, destino) llevan la
 * etiqueta a la IZQUIERDA para no tapar la placa del conductor cercano, que la lleva a la derecha.
 */
export function labelledMarker(className: string, label: string, ariaLabel: string, labelSide: 'left' | 'right' = 'right') {
  const element = document.createElement('div')
  element.className = `map-marker ${className}${labelSide === 'left' ? ' label-left' : ''}`
  element.setAttribute('role', 'img')
  element.setAttribute('aria-label', ariaLabel)
  element.innerHTML = '<span class="map-dot"></span><span class="map-label"></span>'
  element.querySelector('.map-label')!.textContent = label
  return labelSide === 'left'
    ? new maplibregl.Marker({ element, anchor: 'right', offset: [8, 0] })
    : new maplibregl.Marker({ element, anchor: 'left', offset: [-8, 0] })
}
