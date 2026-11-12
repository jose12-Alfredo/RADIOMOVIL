import { useCallback, useEffect, useRef, useState } from 'react'
import { classifySendError, fixFromPosition, geolocationError, sendFix } from './driver'
import type { Fix, GpsReceipt } from './driver'

export type EmitterState = {
  sharing: boolean
  lastFix: Fix | null
  receipt: GpsReceipt | null
  sentCount: number
  issue: string | null
  screenLock: 'activo' | 'no-disponible' | 'inactivo'
}

type WakeLockSentinelLike = { release: () => Promise<void> }
type WakeLockNavigator = Navigator & { wakeLock?: { request: (type: 'screen') => Promise<WakeLockSentinelLike> } }

/**
 * Emisor de prueba: lee la ubicación REAL del dispositivo (Geolocation API, alta precisión) y la envía
 * al backend como máximo cada `intervalSeconds`. Si el teléfono no se mueve y watchPosition calla, pide
 * una lectura nueva con getCurrentPosition para que el servidor no la considere desactualizada.
 * Nunca fabrica, interpola ni reutiliza coordenadas como si fueran nuevas.
 */
export function useGpsEmitter(intervalSeconds: number, handlers: { onNoShift: () => void; onSession: () => void }) {
  const [state, setState] = useState<EmitterState>({ sharing: false, lastFix: null, receipt: null, sentCount: 0, issue: null, screenLock: 'inactivo' })
  const watchId = useRef<number | null>(null)
  const timer = useRef<number | null>(null)
  const lastSentAt = useRef(0)
  const holdUntil = useRef(0)
  const inFlight = useRef(false)
  const wakeLock = useRef<WakeLockSentinelLike | null>(null)
  const sharing = useRef(false)
  const latest = useRef(handlers)
  useEffect(() => { latest.current = handlers })

  const acquireScreen = useCallback(async () => {
    const nav = navigator as WakeLockNavigator
    if (!nav.wakeLock) { setState(s => ({ ...s, screenLock: 'no-disponible' })); return }
    try { wakeLock.current = await nav.wakeLock.request('screen'); setState(s => ({ ...s, screenLock: 'activo' })) }
    catch { setState(s => ({ ...s, screenLock: 'no-disponible' })) }
  }, [])

  const stop = useCallback(() => {
    sharing.current = false
    if (watchId.current !== null) navigator.geolocation.clearWatch(watchId.current)
    if (timer.current !== null) clearInterval(timer.current)
    watchId.current = null; timer.current = null
    void wakeLock.current?.release().catch(() => undefined)
    wakeLock.current = null
    setState(s => ({ ...s, sharing: false, screenLock: 'inactivo' }))
  }, [])

  // Última lectura REAL aún no enviada; se envía al cumplirse el intervalo si sigue siendo reciente.
  const pending = useRef<Fix | null>(null)
  const requesting = useRef(false)

  const trySend = useCallback(async () => {
    const fix = pending.current
    const now = Date.now()
    if (!sharing.current || !fix || inFlight.current || now < holdUntil.current || now - lastSentAt.current < intervalSeconds * 1000) return
    // Una lectura más vieja que dos intervalos ya no representa la posición actual: se pide otra.
    if (now - Date.parse(fix.capturadaEn) > intervalSeconds * 2000) { pending.current = null; return }
    pending.current = null
    inFlight.current = true
    lastSentAt.current = now
    try {
      const receipt = await sendFix(fix)
      if (!sharing.current) return
      setState(s => ({ ...s, receipt, sentCount: s.sentCount + (receipt.aceptada ? 1 : 0), issue: null }))
    } catch (error) {
      const outcome = classifySendError(error)
      if (outcome.kind === 'wait') holdUntil.current = Date.now() + outcome.seconds * 1000
      else if (outcome.kind === 'no-shift') { stop(); setState(s => ({ ...s, issue: outcome.message })); latest.current.onNoShift() }
      else if (outcome.kind === 'session') { stop(); latest.current.onSession() }
      else if (outcome.kind === 'rejected' || outcome.kind === 'offline') setState(s => ({ ...s, issue: outcome.message }))
    } finally {
      inFlight.current = false
    }
  }, [intervalSeconds, stop])

  const onPosition = useCallback((position: GeolocationPosition) => {
    if (!sharing.current) return
    const fix = fixFromPosition(position)
    if (!fix) return
    pending.current = fix
    setState(s => ({ ...s, lastFix: fix }))
    void trySend()
  }, [trySend])

  const onError = useCallback((error: GeolocationPositionError) => {
    setState(s => ({ ...s, issue: geolocationError(error.code) }))
    if (error.code === 1) stop()
  }, [stop])

  const start = useCallback(() => {
    if (!window.isSecureContext) { setState(s => ({ ...s, issue: 'Esta página no se abrió con HTTPS confiable: el navegador no permite leer la ubicación.' })); return }
    if (!('geolocation' in navigator)) { setState(s => ({ ...s, issue: 'Este navegador no ofrece geolocalización.' })); return }
    sharing.current = true
    setState(s => ({ ...s, sharing: true, issue: null }))
    const options: PositionOptions = { enableHighAccuracy: true, maximumAge: 0, timeout: 20_000 }
    watchId.current = navigator.geolocation.watchPosition(onPosition, onError, options)
    // Cada segundo: enviar la lectura pendiente si ya toca; si el teléfono quieto no produjo lecturas, pedir una nueva.
    timer.current = window.setInterval(() => {
      if (pending.current) { void trySend(); return }
      if (!requesting.current && Date.now() - lastSentAt.current >= intervalSeconds * 1000) {
        requesting.current = true
        navigator.geolocation.getCurrentPosition(p => { requesting.current = false; onPosition(p) }, () => { requesting.current = false }, options)
      }
    }, 1000)
    void acquireScreen()
  }, [acquireScreen, intervalSeconds, onError, onPosition, trySend])

  // El navegador libera el bloqueo de pantalla al ocultar la página: recuperarlo al volver.
  useEffect(() => {
    const recover = () => { if (document.visibilityState === 'visible' && sharing.current) void acquireScreen() }
    document.addEventListener('visibilitychange', recover)
    return () => document.removeEventListener('visibilitychange', recover)
  }, [acquireScreen])

  useEffect(() => stop, [stop])
  return { ...state, start, stop }
}
