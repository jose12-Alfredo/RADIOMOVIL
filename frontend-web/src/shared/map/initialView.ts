// Vista inicial del mapa de registro: PREFERENCIA LOCAL de este navegador, guardada solo cuando la operadora pulsa
// «Usar esta vista al abrir». Es el encuadre del área de trabajo, nunca la ubicación de un pasajero ni de un
// conductor, y no es un dato sensible. La central aún no tiene «área operativa» en la API (cambio de contrato pendiente).

export type MapView = { lat: number; lng: number; zoom: number }

const KEY = 'radiomovil.mapa.vistaInicial.v1'

export function isMapView(value: unknown): value is MapView {
  if (typeof value !== 'object' || value === null) return false
  const v = value as Record<string, unknown>
  return typeof v.lat === 'number' && typeof v.lng === 'number' && typeof v.zoom === 'number'
    && Math.abs(v.lat) <= 85 && Math.abs(v.lng) <= 180 && v.zoom >= 3 && v.zoom <= 19
}

export function loadInitialView(): MapView | null {
  try {
    const raw = window.localStorage.getItem(KEY)
    if (!raw) return null
    const parsed: unknown = JSON.parse(raw)
    return isMapView(parsed) ? parsed : null
  } catch { return null }
}

/** Devuelve false si el navegador no permite guardar (modo privado, almacenamiento bloqueado). */
export function saveInitialView(view: MapView): boolean {
  if (!isMapView(view)) return false
  try {
    window.localStorage.setItem(KEY, JSON.stringify({ lat: +view.lat.toFixed(5), lng: +view.lng.toFixed(5), zoom: +view.zoom.toFixed(1) }))
    return true
  } catch { return false }
}
