import { useCallback, useRef, useState } from 'react'
import { useAuth } from '../auth/auth-context'
import { classifyActionError } from './fleet'
import type { OperationResult } from './fleet'

type Pending<T> = { key: string; input: T }
export type ActionState<T> =
  | { status: 'idle' }
  | { status: 'sending'; pending: Pending<T> }
  | { status: 'uncertain'; pending: Pending<T>; message: string }
  | { status: 'rejected'; message: string }
  | { status: 'done'; result: OperationResult; input: T }

/**
 * Una operación de flota con Idempotency-Key. Mientras el resultado sea dudoso se conservan
 * la misma clave y los mismos datos: reintentar nunca duplica un alta ni un turno.
 * Nada se da por aplicado hasta que el servidor lo confirma.
 */
export function useIdempotentAction<T>(run: (input: T, key: string) => Promise<OperationResult>, onDone?: (result: OperationResult, input: T) => void) {
  const { revalidate } = useAuth()
  const [state, setState] = useState<ActionState<T>>({ status: 'idle' })
  const busy = useRef(false)

  const send = useCallback(async (pending: Pending<T>) => {
    if (busy.current) return
    busy.current = true
    setState({ status: 'sending', pending })
    try {
      const result = await run(pending.input, pending.key)
      setState({ status: 'done', result, input: pending.input })
      onDone?.(result, pending.input)
    } catch (error) {
      const failure = classifyActionError(error)
      if (failure.kind === 'session') { setState({ status: 'idle' }); void revalidate() }
      else if (failure.kind === 'uncertain') setState({ status: 'uncertain', pending, message: failure.message })
      else setState({ status: 'rejected', message: failure.message })
    } finally {
      busy.current = false
    }
  }, [run, onDone, revalidate])

  const submit = useCallback((input: T) => { void send({ key: crypto.randomUUID(), input }) }, [send])
  const retry = useCallback(() => { if (state.status === 'uncertain') void send(state.pending) }, [send, state])
  const reset = useCallback(() => setState({ status: 'idle' }), [])
  return { state, submit, retry, reset, sending: state.status === 'sending', locked: state.status === 'sending' || state.status === 'uncertain' }
}
