import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../auth/auth-context'
import { useApiQuery } from '../../shared/hooks/useApiQuery'
import { Icon } from '../../shared/components/Icon'
import { classifyCreateError, createRequest, listCentrales, originMaxLength, resolveCentralId, validateOrigin } from './requests'
import type { CreateRequestInput, ServiceRequest } from './requests'
import type { LngLat, PickTarget } from '../dispatch/maps'
import { LocationDialog } from './LocationDialog'
import type { Points } from './LocationDialog'
import { formatCoords } from './location'
const configuredCentral = resolveCentralId(import.meta.env.VITE_CENTRAL_ID)
type Pending = { key: string; input: CreateRequestInput }
// ~0,1 m: suficiente para recoger y estable al reintentar con la misma clave.
const round = (value: number) => Math.round(value * 1e6) / 1e6
const point = (lat?: number | null, lng?: number | null): LngLat | null => lat == null || lng == null ? null : [lng, lat]

/**
 * La misma clave y el mismo contenido se conservan mientras el resultado sea dudoso:
 * reintentar nunca crea otra solicitud, y el campo queda bloqueado hasta resolverlo.
 */
export function RegisterPanel({ onRegistered }: { onRegistered: (created: ServiceRequest) => void }) {
  const { revalidate } = useAuth()
  const [origen, setOrigen] = useState('')
  const [central, setCentral] = useState('')
  const [telefono, setTelefono] = useState('')
  const [nombre, setNombre] = useState('')
  const [destino, setDestino] = useState('')
  const [observaciones, setObservaciones] = useState('')
  const [pickup, setPickup] = useState<LngLat | null>(null)
  const [destination, setDestination] = useState<LngLat | null>(null)
  const [mapOpen, setMapOpen] = useState(false)
  const mapButton = useRef<HTMLButtonElement>(null)
  const centrales = useApiQuery(signal => listCentrales(signal), 'centrales', 60_000)
  const selected = central || (configuredCentral && centrales.data?.some(c => c.id === configuredCentral) ? configuredCentral : '') || (centrales.data?.length === 1 ? centrales.data[0].id : '')
  const [sending, setSending] = useState(false)
  const [fieldError, setFieldError] = useState<string | null>(null)
  const [rejected, setRejected] = useState<string | null>(null)
  const [uncertain, setUncertain] = useState<{ pending: Pending; message: string } | null>(null)
  const [confirmed, setConfirmed] = useState<ServiceRequest | null>(null)
  const input = useRef<HTMLInputElement>(null)
  const retry = useRef<HTMLButtonElement>(null)

  // Con resultado dudoso, Enter no debe quedar sin efecto: la acción segura recibe el foco.
  useEffect(() => { if (uncertain) retry.current?.focus() }, [uncertain])

  // El foco se pide tras aplicar el cambio: antes, el campo aún podría estar deshabilitado.
  const [focusRequest, setFocusRequest] = useState(0)
  useEffect(() => { if (focusRequest > 0) input.current?.focus() }, [focusRequest])

  useEffect(() => {
    // En táctil, enfocar abriría el teclado sobre la cola nada más entrar.
    if (selected && window.matchMedia('(pointer: fine)').matches) input.current?.focus()
  }, [selected])

  async function send(pending: Pending) {
    if (!pending.input.centralId || sending) return
    setSending(true)
    setRejected(null)
    try {
      const created = await createRequest(pending.input, pending.key)
      setUncertain(null)
      setConfirmed(created)
      setOrigen('')
      setTelefono(''); setNombre(''); setDestino(''); setObservaciones('')
      setPickup(null); setDestination(null)
      onRegistered(created)
      setSending(false)
      setFocusRequest(request => request + 1)
    } catch (error) {
      setSending(false)
      const failure = classifyCreateError(error)
      if (failure.kind === 'session') { void revalidate(); return }
      if (failure.kind === 'uncertain') { setUncertain({ pending, message: failure.message }); return }
      setUncertain(null)
      setRejected(failure.message)
      setFocusRequest(request => request + 1)
    }
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (uncertain) return
    const problem = validateOrigin(origen)
    setFieldError(problem)
    if (problem) { input.current?.focus(); return }
    setConfirmed(null)
    if (!selected) { setRejected('Selecciona una central autorizada.'); return }
    void send({ key: crypto.randomUUID(), input: { centralId: selected, origen: origen.trim(),
      telefonoPasajero: telefono.trim() || null, nombrePasajero: nombre.trim() || null,
      destino: destino.trim() || null, observaciones: observaciones.trim() || null,
      latitud: pickup ? round(pickup[1]) : null, longitud: pickup ? round(pickup[0]) : null,
      destinoLatitud: destination ? round(destination[1]) : null, destinoLongitud: destination ? round(destination[0]) : null } })
  }

  function confirmPoints(points: Points) {
    setPickup(points.recogida); setDestination(points.destino)
    setRejected(null)
    closeMap()
  }

  function closeMap() {
    setMapOpen(false)
    // El foco vuelve al botón que abrió el mapa (requestAnimationFrame: el diálogo ya se cerró).
    requestAnimationFrame(() => mapButton.current?.focus())
  }

  function discard() {
    setUncertain(null)
    setOrigen('')
    setPickup(null); setDestination(null)
    setFocusRequest(request => request + 1)
  }

  const blocked = !selected || !!centrales.error
  const shownPickup = uncertain ? point(uncertain.pending.input.latitud, uncertain.pending.input.longitud) : pickup
  const shownDestination = uncertain ? point(uncertain.pending.input.destinoLatitud, uncertain.pending.input.destinoLongitud) : destination
  return <section className="register-panel" aria-labelledby="register-title">
    <h2 id="register-title">Nueva solicitud</h2>
    {blocked && <div className="work-notice" role="status">
      <Icon name="alert" />
      <div className="work-notice-body"><p><strong>Registro no disponible</strong></p>
        <p>{centrales.error ? 'No se pudieron consultar las centrales autorizadas.' : centrales.loading ? 'Consultando centrales…' : centrales.data?.length ? 'Selecciona una central.' : 'No hay centrales activas. Pide a un administrador que registre una.'}</p>
        {centrales.error != null && <button type="button" className="text-action" onClick={centrales.refresh}>Reintentar consulta</button>}</div>
    </div>}
    <form noValidate onSubmit={submit} aria-busy={sending}>
      <div className="field"><label htmlFor="central">Central</label>
        <select id="central" value={selected} onChange={event => setCentral(event.target.value)} disabled={sending || !!uncertain || !centrales.data?.length}>
          {centrales.data?.length !== 1 && <option value="">Selecciona una central</option>}
          {centrales.data?.map(item => <option value={item.id} key={item.id}>{item.nombre}</option>)}
        </select></div>
      <div className="field">
        <label htmlFor="origen">Origen</label>
        <input ref={input} id="origen" name="origen" type="text" autoComplete="off" maxLength={originMaxLength}
          placeholder="Dirección o punto de recogida" value={uncertain ? uncertain.pending.input.origen : origen}
          onChange={event => { setOrigen(event.target.value); setFieldError(null); setRejected(null) }}
          readOnly={!!uncertain} disabled={blocked || sending} aria-invalid={!!fieldError}
          aria-describedby={fieldError ? 'origen-error' : undefined} />
        {fieldError && <p className="field-error" id="origen-error">{fieldError}</p>}
      </div>
      <MapPoints pickup={shownPickup} destination={shownDestination} locked={blocked || sending || !!uncertain} buttonRef={mapButton}
        onOpen={() => setMapOpen(true)} onClear={which => which === 'recogida' ? setPickup(null) : setDestination(null)} />
      <details className="register-extra"><summary>Datos adicionales (opcionales)</summary>
      <div className="field"><label htmlFor="telefono">Teléfono del pasajero</label>
        <input id="telefono" type="tel" maxLength={40} value={uncertain ? uncertain.pending.input.telefonoPasajero ?? '' : telefono} onChange={event => setTelefono(event.target.value)} disabled={blocked || sending} readOnly={!!uncertain} /></div>
      <div className="field"><label htmlFor="pasajero">Nombre del pasajero</label>
        <input id="pasajero" maxLength={160} value={uncertain ? uncertain.pending.input.nombrePasajero ?? '' : nombre} onChange={event => setNombre(event.target.value)} disabled={blocked || sending} readOnly={!!uncertain} /></div>
      <div className="field"><label htmlFor="destino">Destino</label>
        <input id="destino" maxLength={500} value={uncertain ? uncertain.pending.input.destino ?? '' : destino} onChange={event => setDestino(event.target.value)} disabled={blocked || sending} readOnly={!!uncertain} /></div>
      <div className="field"><label htmlFor="observaciones">Observaciones</label>
        <textarea id="observaciones" rows={3} maxLength={1000} value={uncertain ? uncertain.pending.input.observaciones ?? '' : observaciones} onChange={event => setObservaciones(event.target.value)} disabled={blocked || sending} readOnly={!!uncertain} /></div>
      </details>
      {!uncertain && <button className="primary-button" type="submit" disabled={blocked || sending}>
        {sending ? 'Registrando…' : 'Registrar solicitud'}
      </button>}
    </form>
    {/* Fuera del formulario: su búsqueda tiene su propio envío y nunca debe registrar la solicitud. */}
    <LocationDialog open={mapOpen} initial={{ recogida: shownPickup, destino: shownDestination }} origin={uncertain ? uncertain.pending.input.origen : origen}
      readOnly={!!uncertain || sending} onConfirm={confirmPoints} onClose={closeMap} />

    {uncertain && <div className="work-notice work-notice-warning" role="alert">
      <Icon name="alert" />
      <div className="work-notice-body">
        <p><strong>No sabemos si se registró</strong></p>
        <p>{uncertain.message} Reintentar no creará un duplicado.</p>
        <button ref={retry} type="button" className="primary-button" onClick={() => void send(uncertain.pending)} disabled={sending}>{sending ? 'Reintentando…' : 'Reintentar'}</button>
        <button type="button" className="text-action" onClick={discard} disabled={sending}>Descartar y atender otra llamada</button>
      </div>
    </div>}
    {rejected && <div className="work-notice work-notice-error" role="alert"><Icon name="alert" /><div className="work-notice-body"><p>{rejected}</p></div></div>}
    <div className="register-result" role="status">
      {confirmed && !uncertain && !rejected && <div className="work-notice work-notice-success">
        <Icon name="check" />
        <div className="work-notice-body">
          <p><strong>Registrada · Orden <span className="tabular">{confirmed.ordenRecepcion}</span></strong></p>
          <p className="confirmed-origin">{confirmed.origen}</p>
          <Link className="text-action" to={`/solicitudes/${confirmed.id}`}>Ver solicitud</Link>
        </div>
      </div>}
    </div>
  </section>
}

const targetLabel: Record<PickTarget, string> = { recogida: 'Recogida', destino: 'Destino' }

/** Resumen de los puntos exactos (opcionales: sin ellos el conductor solo ve el texto) y acceso al mapa grande. */
function MapPoints({ pickup, destination, locked, buttonRef, onOpen, onClear }: {
  pickup: LngLat | null; destination: LngLat | null; locked: boolean; buttonRef: { current: HTMLButtonElement | null }
  onOpen: () => void; onClear: (t: PickTarget) => void
}) {
  const marked = { recogida: pickup, destino: destination }
  return <div className="map-points" role="group" aria-labelledby="map-points-title">
    <span id="map-points-title" className="map-points-title">Ubicación en el mapa</span>
    <ul className="map-points-list">
      {(['recogida', 'destino'] as const).map(key => <li key={key}>
        <span className={`map-key map-key-${key}`} aria-hidden="true" />
        <span className="map-points-name">{targetLabel[key]}</span>
        <span className={`map-points-state${marked[key] ? ' tabular is-marked' : ''}`}>
          {marked[key] ? formatCoords(marked[key][1], marked[key][0]) : key === 'recogida' ? 'Sin marcar' : 'Opcional'}</span>
        {marked[key] && !locked && <button type="button" className="text-action" onClick={() => onClear(key)} aria-label={`Quitar ${targetLabel[key].toLowerCase()}`}>Quitar</button>}
      </li>)}
    </ul>
    <button ref={buttonRef} type="button" className="secondary-button map-open" onClick={onOpen} disabled={locked && !pickup && !destination}>
      <Icon name="despacho" />{locked ? 'Ver en el mapa grande' : 'Abrir mapa grande'}</button>
    {!pickup && <p className="field-help">Pega ahí el enlace de WhatsApp o las coordenadas. Sin punto, el conductor solo verá la dirección escrita.</p>}
  </div>
}
