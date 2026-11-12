import { lazy, Suspense, useCallback, useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Icon } from '../../shared/components/Icon'
import { saveInitialView } from '../../shared/map/initialView'
import type { LngLat, PickerControls, PickTarget } from '../dispatch/maps'
import { formatCoords, parseLocation } from './location'

const PointPickerMap = lazy(() => import('../dispatch/maps').then(m => ({ default: m.PointPickerMap })))

export type Points = { recogida: LngLat | null; destino: LngLat | null }
const label: Record<PickTarget, string> = { recogida: 'Recogida', destino: 'Destino' }
const same = (a: LngLat | null, b: LngLat | null) => a === b || (!!a && !!b && a[0] === b[0] && a[1] === b[1])
// ~0,1 m: suficiente para recoger y estable al reintentar con la misma clave.
const round = (p: LngLat): LngLat => [Math.round(p[0] * 1e6) / 1e6, Math.round(p[1] * 1e6) / 1e6]

type Feedback = { tone: 'ok' | 'error' | 'info'; text: string } | null

/**
 * Selector amplio de recogida y destino. Trabaja sobre un BORRADOR: nada cambia en la solicitud hasta
 * «Confirmar ubicación»; cerrar sin confirmar conserva lo que ya estaba confirmado.
 */
export function LocationDialog({ open, initial, origin, readOnly, onConfirm, onClose }: {
  open: boolean; initial: Points; origin: string; readOnly: boolean
  onConfirm: (points: Points) => void; onClose: () => void
}) {
  const dialog = useRef<HTMLDialogElement>(null)
  const requestClose = useRef<(() => void) | null>(null)
  useEffect(() => {
    const element = dialog.current
    if (!element) return
    if (open && !element.open) element.showModal()
    if (!open && element.open) element.close()
  }, [open])
  return <dialog ref={dialog} className="location-dialog" aria-labelledby="location-title"
    onCancel={event => { event.preventDefault(); (requestClose.current ?? onClose)() }}>
    {open && <LocationEditor initial={initial} origin={origin} readOnly={readOnly} onConfirm={onConfirm} onClose={onClose} registerClose={fn => { requestClose.current = fn }} />}
  </dialog>
}

function LocationEditor({ initial, origin, readOnly, onConfirm, onClose, registerClose }: {
  initial: Points; origin: string; readOnly: boolean; onConfirm: (points: Points) => void; onClose: () => void
  registerClose: (close: (() => void) | null) => void
}) {
  const [draft, setDraft] = useState<Points>(initial)
  const [target, setTarget] = useState<PickTarget>(initial.recogida ? (initial.destino ? 'recogida' : 'destino') : 'recogida')
  const [query, setQuery] = useState('')
  const [feedback, setFeedback] = useState<Feedback>(null)
  const [focus, setFocus] = useState<{ point: LngLat; n: number } | null>(null)
  const [confirmDiscard, setConfirmDiscard] = useState(false)
  const controls = useRef<PickerControls | null>(null)
  const onControls = useCallback((c: PickerControls | null) => { controls.current = c }, [])
  const search = useRef<HTMLInputElement>(null)
  const keepEditing = useRef<HTMLButtonElement>(null)
  const dirty = !same(draft.recogida, initial.recogida) || !same(draft.destino, initial.destino)

  // Con ratón, el cursor va directo al campo de pegar; en táctil no se abre el teclado sobre el mapa.
  useEffect(() => {
    if (readOnly || !window.matchMedia('(pointer: fine)').matches) return
    // Tras showModal (efecto del padre), que enfoca el primer control del diálogo.
    const frame = requestAnimationFrame(() => search.current?.focus())
    return () => cancelAnimationFrame(frame)
  }, [readOnly])
  useEffect(() => { if (confirmDiscard) keepEditing.current?.focus() }, [confirmDiscard])

  // Escape o «Volver» con cambios sin confirmar pide confirmación en vez de perderlos.
  const close = () => { if (dirty && !readOnly) setConfirmDiscard(true); else onClose() }
  useEffect(() => { registerClose(close); return () => registerClose(null) })

  function place(which: PickTarget, point: LngLat) {
    setDraft(current => ({ ...current, [which]: round(point) }))
    setTarget(which)
    setConfirmDiscard(false)
  }

  function locate(event: FormEvent) {
    event.preventDefault()
    event.stopPropagation()
    const parsed = parseLocation(query)
    if (!parsed.ok) { setFeedback({ tone: 'error', text: parsed.error }); return }
    const point: LngLat = [parsed.lng, parsed.lat]
    place(target, point)
    setFocus(current => ({ point, n: (current?.n ?? 0) + 1 }))
    setFeedback({ tone: 'ok', text: `${label[target]} ${parsed.source === 'enlace' ? 'tomada del enlace' : 'ubicada'}: ${formatCoords(parsed.lat, parsed.lng)}. Arrastra el marcador si hace falta ajustarla.` })
    setQuery('')
  }

  function saveView() {
    const view = controls.current?.getView()
    const saved = !!view && view.zoom >= 3 && saveInitialView(view)
    setFeedback(saved ? { tone: 'info', text: 'Este encuadre se usará al abrir el mapa en este navegador. No marca ningún punto.' }
      : { tone: 'error', text: 'No se pudo guardar la vista: acerca más el mapa o revisa que el navegador permita guardar datos.' })
  }

  return <div className={`location-shell${readOnly ? ' is-readonly' : ''}`}>
    <header className="location-head">
      <button type="button" className="icon-button" onClick={close} aria-label={readOnly ? 'Cerrar mapa' : 'Volver al registro'}><Icon name="back" /></button>
      <div className="location-heading">
        <h2 id="location-title">{readOnly ? 'Ubicación registrada' : 'Ubicación del pasajero'}</h2>
        {origin.trim() && <p className="location-origin" title={origin}>{origin}</p>}
      </div>
    </header>

    <div className="location-side">
      {!readOnly && <>
        <div className="location-targets" role="radiogroup" aria-label="Punto que estás editando">
          {(['recogida', 'destino'] as const).map(key => <button key={key} type="button" role="radio" aria-checked={target === key}
            className={`location-target target-${key}`} onClick={() => { setTarget(key); setFeedback(null) }}>
            <span className={`map-key map-key-${key}`} aria-hidden="true" />
            <span className="location-target-name">{label[key]}{key === 'destino' && <span className="location-optional"> · opcional</span>}</span>
            <span className="location-target-value tabular">{draft[key] ? formatCoords(draft[key][1], draft[key][0]) : 'Sin marcar'}</span>
          </button>)}
        </div>

        <form className="location-search" onSubmit={locate} noValidate>
          <label htmlFor="location-query">Pegar enlace o coordenadas para <strong>{label[target].toLowerCase()}</strong></label>
          <div className="location-search-row">
            <input ref={search} id="location-query" type="text" inputMode="text" autoComplete="off" spellCheck={false}
              placeholder="Enlace o lat, lng" value={query} enterKeyHint="search"
              onChange={event => { setQuery(event.target.value); if (feedback?.tone === 'error') setFeedback(null) }}
              aria-invalid={feedback?.tone === 'error'} aria-describedby="location-feedback" />
            <button type="submit" className="secondary-button" disabled={!query.trim()}>Ubicar</button>
          </div>
        </form>
      </>}

      <div id="location-feedback" className="location-feedback" role="status" aria-live="polite">
        {feedback && <p className={`location-feedback-${feedback.tone}`}>{feedback.text}</p>}
      </div>

      {readOnly ? <ul className="location-readonly">
        {(['recogida', 'destino'] as const).map(key => <li key={key}><span className={`map-key map-key-${key}`} aria-hidden="true" />
          <span>{label[key]}</span><span className="tabular">{draft[key] ? formatCoords(draft[key][1], draft[key][0]) : 'Sin marcar'}</span></li>)}
      </ul> : <div className="location-tools">
        {draft[target] && <button type="button" className="text-action" onClick={() => { setDraft(c => ({ ...c, [target]: null })); setFeedback(null) }}>
          Quitar {label[target].toLowerCase()}</button>}
        {target === 'recogida' && draft.recogida && !draft.destino && <button type="button" className="text-action" onClick={() => setTarget('destino')}>Marcar también el destino</button>}
        <p className="field-help">Toca el mapa para colocar <strong>{label[target].toLowerCase()}</strong>; el otro punto no cambia. Arrastra un marcador para ajustarlo. Con teclado: flechas para mover el mapa y «Marcar el centro».</p>
        <button type="button" className="text-action location-save-view" onClick={saveView}>Usar esta vista al abrir el mapa</button>
      </div>}
    </div>

    <div className="location-map">
      {!readOnly && <p className={`location-editing target-${target}`} aria-hidden="true"><span className={`map-key map-key-${target}`} />Tocar coloca: {label[target]}</p>}
      <Suspense fallback={<div className="picker-loading" role="status">Cargando mapa…</div>}>
        <PointPickerMap pickup={draft.recogida} destination={draft.destino} target={target} readOnly={readOnly}
          onPick={place} onSelectTarget={key => { setTarget(key); setFeedback(null) }} focus={focus} onControls={onControls} />
      </Suspense>
    </div>

    <footer className="location-foot">
      {confirmDiscard ? <div className="location-discard" role="alertdialog" aria-labelledby="discard-text">
        <p id="discard-text">¿Salir sin guardar los cambios del mapa?</p>
        <button ref={keepEditing} type="button" className="secondary-button" onClick={() => setConfirmDiscard(false)}>Seguir editando</button>
        <button type="button" className="secondary-button danger" onClick={onClose}>Descartar cambios</button>
      </div> : readOnly ? <button type="button" className="primary-button" onClick={onClose}>Cerrar</button> : <>
        <button type="button" className="secondary-button" onClick={close}>Cancelar</button>
        <button type="button" className="primary-button" onClick={() => onConfirm(draft)}>
Confirmar ubicación</button>
      </>}
    </footer>
  </div>
}
