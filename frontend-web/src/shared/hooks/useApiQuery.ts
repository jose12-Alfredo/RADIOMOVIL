import { useCallback, useEffect, useRef, useState } from 'react'
import { useAuth } from '../../features/auth/auth-context'
import { HttpError } from '../../services/http'

export type ApiQuery<T> = {
  data: T | null
  error: unknown
  /** Primera carga, sin datos que mostrar todavía. */
  loading: boolean
  refreshing: boolean
  updatedAt: Date | null
  refresh: () => void
}

/**
 * Consulta GET con refresco al volver a la pestaña y por intervalo si está visible.
 * Conserva los últimos datos ante un fallo para distinguir "desactualizado" de "vacío".
 * No hay tiempo real: SignalR pertenece a otra fase.
 */
export function useApiQuery<T>(fetcher: (signal: AbortSignal) => Promise<T>, key: string, intervalMs = 0): ApiQuery<T> {
  const { revalidate } = useAuth()
  const [state, setState] = useState<{ key: string; data: T | null; error: unknown; updatedAt: Date | null; refreshing: boolean }>(
    { key, data: null, error: null, updatedAt: null, refreshing: true })
  const latest = useRef(fetcher)
  useEffect(() => { latest.current = fetcher })
  const active = useRef<AbortController | null>(null)

  const load = useCallback(async () => {
    active.current?.abort()
    const controller = new AbortController()
    active.current = controller
    setState(previous => ({ ...previous, refreshing: true }))
    try {
      const data = await latest.current(controller.signal)
      if (!controller.signal.aborted) setState({ key, data, error: null, updatedAt: new Date(), refreshing: false })
    } catch (error) {
      if (controller.signal.aborted) return
      if (error instanceof HttpError && error.status === 401) { void revalidate(); return }
      setState(previous => ({ ...previous, key, data: previous.key === key ? previous.data : null,
        updatedAt: previous.key === key ? previous.updatedAt : null, error, refreshing: false }))
    }
  }, [key, revalidate])

  useEffect(() => {
    // Sincroniza con el servidor al montar o cambiar de consulta.
    // oxlint-disable-next-line react/set-state-in-effect
    void load()
    const whenVisible = () => { if (document.visibilityState === 'visible') void load() }
    document.addEventListener('visibilitychange', whenVisible)
    const timer = intervalMs > 0 ? window.setInterval(whenVisible, intervalMs) : undefined
    return () => {
      document.removeEventListener('visibilitychange', whenVisible)
      if (timer !== undefined) clearInterval(timer)
      active.current?.abort()
    }
  }, [load, intervalMs])

  const refresh = useCallback(() => { void load() }, [load])
  const current = state.key === key
  const data = current ? state.data : null
  return {
    data, error: current ? state.error : null, updatedAt: current ? state.updatedAt : null,
    loading: data === null && (!current || state.refreshing), refreshing: state.refreshing,
    refresh,
  }
}
