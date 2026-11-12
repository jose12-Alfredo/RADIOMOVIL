// Interpreta lo que la operadora pega (WhatsApp, Google Maps, coordenadas escritas) SIN red:
// solo se aceptan coordenadas que el propio texto contiene. Nunca se geocodifica ni se sigue un enlace.

export type ParsedLocation =
  | { ok: true; lat: number; lng: number; source: 'coordenadas' | 'enlace' }
  | { ok: false; error: string }

const fail = (error: string): ParsedLocation => ({ ok: false, error })

const NUM = String.raw`[+-]?\d{1,3}(?:\.\d+)?`
const PAIR = new RegExp(String.raw`^\s*(${NUM})\s*[,;\s]\s*(${NUM})\s*$`)
// 12°02'47.0"S 77°02'34.1"W (formato que muestra Google Maps al tocar un punto).
const DMS_PART = String.raw`(\d{1,3})\s*°\s*(?:(\d{1,2}(?:\.\d+)?)\s*['′’]\s*)?(?:(\d{1,2}(?:\.\d+)?)\s*(?:"|″|”|''|′′)\s*)?([NSEWO])`
const DMS = new RegExp(String.raw`^\s*${DMS_PART}[\s,]+${DMS_PART}\s*$`, 'i')

const SHORT_HOSTS = ['maps.app.goo.gl', 'goo.gl', 'g.co']

function inRange(lat: number, lng: number): ParsedLocation {
  if (!Number.isFinite(lat) || !Number.isFinite(lng)) return fail('Las coordenadas no son números válidos.')
  if (lat < -90 || lat > 90) return fail('La latitud debe estar entre -90 y 90. Revisa el orden: primero latitud, después longitud.')
  if (lng < -180 || lng > 180) return fail('La longitud debe estar entre -180 y 180.')
  if (lat === 0 && lng === 0) return fail('0, 0 no es una ubicación de recogida: el enlace o el texto no trae coordenadas reales.')
  return { ok: true, lat, lng, source: 'coordenadas' }
}

function pair(text: string): ParsedLocation | null {
  const match = PAIR.exec(text)
  return match ? inRange(Number(match[1]), Number(match[2])) : null
}

function dms(text: string): ParsedLocation | null {
  const match = DMS.exec(text.replace(/\s+/g, ' '))
  if (!match) return null
  const part = (deg: string, min = '0', sec = '0', hemi: string) => {
    if (Number(min) >= 60 || Number(sec) >= 60) return Number.NaN
    const value = Number(deg) + Number(min) / 60 + Number(sec) / 3600
    return /[SWO]/i.test(hemi) ? -value : value
  }
  const [a, b] = [part(match[1], match[2], match[3], match[4]), part(match[5], match[6], match[7], match[8])]
  const firstIsLat = /[NS]/i.test(match[4])
  if (firstIsLat === /[NS]/i.test(match[8])) return fail('Las coordenadas en grados necesitan una latitud (N/S) y una longitud (E/W).')
  const result = firstIsLat ? inRange(a, b) : inRange(b, a)
  return result.ok ? { ...result, lat: Math.round(result.lat * 1e6) / 1e6, lng: Math.round(result.lng * 1e6) / 1e6 } : result
}

function isGoogleMapsHost(host: string) {
  return /^(?:www\.|maps\.)?google\.[a-z]{2,3}(?:\.[a-z]{2})?$/.test(host)
}

/** Coordenadas explícitas en un enlace de Google Maps, en orden de precisión. */
function fromGoogleUrl(url: URL): ParsedLocation {
  let text: string
  try { text = decodeURIComponent(url.pathname + url.search + url.hash).replace(/\+/g, ' ') }
  catch { return fail('El enlace está incompleto o mal copiado.') }
  // !3d!4d es el lugar marcado; @lat,lng es solo el centro de la vista y va después.
  const place = /!3d(-?\d+(?:\.\d+)?)!4d(-?\d+(?:\.\d+)?)/.exec(text)
  if (place) return asLink(inRange(Number(place[1]), Number(place[2])))
  for (const key of ['q', 'query', 'll', 'destination', 'daddr', 'center', 'sll']) {
    const value = url.searchParams.get(key)
    const parsed = value ? pair(value.replace(/^loc:/i, '')) : null
    if (parsed?.ok) return asLink(parsed)
  }
  const segment = /\/(?:place|search)\/(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)(?:[/?@]|$)/.exec(text)
  if (segment) return asLink(inRange(Number(segment[1]), Number(segment[2])))
  const view = /@(-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?)(?:,|$|\/)/.exec(text)
  if (view) return asLink(inRange(Number(view[1]), Number(view[2])))
  return fail('Este enlace de Google Maps no incluye coordenadas (por ejemplo, solo trae un nombre o una dirección). Pide la ubicación compartida o copia las coordenadas desde Google Maps.')
}

const asLink = (parsed: ParsedLocation): ParsedLocation => parsed.ok ? { ...parsed, source: 'enlace' } : parsed

/**
 * Admite: «lat, lng», «lat lng», grados/minutos/segundos, geo:lat,lng y enlaces de Google Maps con coordenadas
 * (también dentro de un mensaje de WhatsApp). Los enlaces abreviados se rechazan con instrucciones: resolverlos
 * exige consultar a Google (servicio externo pendiente de autorización).
 */
export function parseLocation(raw: string): ParsedLocation {
  const text = raw.trim()
  if (!text) return fail('Pega un enlace de Google Maps o escribe las coordenadas.')
  if (text.length > 4000) return fail('El texto es demasiado largo para ser una ubicación.')
  const direct = pair(text) ?? dms(text)
  if (direct) return direct
  const geo = /\bgeo:\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)/i.exec(text)
  if (geo) return asLink(inRange(Number(geo[1]), Number(geo[2])))
  const link = /https?:\/\/[^\s<>"']+/i.exec(text)?.[0] ?? (/^(?:www\.|maps\.)?google\.|^maps\.app\.goo\.gl/i.test(text) ? `https://${text}` : null)
  if (!link) return fail('No reconocemos el formato. Usa «latitud, longitud» (por ejemplo -12.04637, -77.04279) o un enlace de Google Maps.')
  let url: URL
  try { url = new URL(link) } catch { return fail('El enlace está incompleto o mal copiado.') }
  const host = url.hostname.toLowerCase()
  if (SHORT_HOSTS.includes(host)) return fail('Los enlaces abreviados (maps.app.goo.gl) no traen las coordenadas dentro. Ábrelo en el teléfono o navegador, copia las coordenadas o el enlace completo y pégalo aquí.')
  if (!isGoogleMapsHost(host)) return fail('Solo se interpretan enlaces de Google Maps. Este enlace es de otro sitio.')
  return fromGoogleUrl(url)
}

/** Texto que la operadora puede leer en voz alta y comparar. */
export function formatCoords(lat: number, lng: number) {
  return `${lat.toFixed(6)}, ${lng.toFixed(6)}`
}
