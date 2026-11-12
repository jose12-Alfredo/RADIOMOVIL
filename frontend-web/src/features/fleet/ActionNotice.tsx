import { useEffect, useRef } from 'react'
import { Icon } from '../../shared/components/Icon'
import type { ActionState } from './useIdempotentAction'

/** Resultado dudoso o rechazado de una operación de flota. La confirmación la anuncia cada pantalla. */
export function ActionNotice<T>({ state, onRetry, onDiscard, discardLabel = 'Descartar' }:
  { state: ActionState<T>; onRetry: () => void; onDiscard: () => void; discardLabel?: string }) {
  const retry = useRef<HTMLButtonElement>(null)
  const uncertain = state.status === 'uncertain'
  useEffect(() => { if (uncertain) retry.current?.focus() }, [uncertain])
  if (state.status === 'uncertain') {
    return <div className="work-notice work-notice-warning" role="alert">
      <Icon name="alert" />
      <div className="work-notice-body">
        <p><strong>No sabemos si se aplicó</strong></p>
        <p>{state.message} Reintentar no lo duplicará.</p>
        <div className="notice-actions">
          <button ref={retry} type="button" className="secondary-button" onClick={onRetry}>Reintentar</button>
          <button type="button" className="text-action" onClick={onDiscard}>{discardLabel}</button>
        </div>
      </div>
    </div>
  }
  if (state.status === 'rejected') return <div className="work-notice work-notice-error" role="alert">
    <Icon name="alert" /><div className="work-notice-body"><p>{state.message}</p></div>
  </div>
  return null
}
