import type { ReactNode } from 'react'
import { formatTime } from '../format'
import { Icon } from './Icon'

export function ErrorState({ message, onRetry, busy }: { message: string; onRetry?: () => void; busy?: boolean }) {
  return <div className="work-notice work-notice-error" role="alert">
    <Icon name="alert" />
    <div className="work-notice-body"><p>{message}</p>
      {onRetry && <button type="button" className="text-action" onClick={onRetry} disabled={busy}>{busy ? 'Reintentando…' : 'Reintentar'}</button>}
    </div>
  </div>
}

export function EmptyState({ title, children }: { title: string; children?: ReactNode }) {
  return <div className="empty-state"><h3>{title}</h3>{children && <p>{children}</p>}</div>
}

export function SkeletonRows({ rows = 5 }: { rows?: number }) {
  return <div className="skeleton-rows" role="status" aria-label="Cargando">
    {Array.from({ length: rows }, (_, index) => <div key={index} className="skeleton-row" aria-hidden="true" />)}
  </div>
}

export function UpdatedAt({ updatedAt, refreshing, stale, onRefresh }: { updatedAt: Date | null; refreshing: boolean; stale: boolean; onRefresh: () => void }) {
  if (!updatedAt) return null
  return <div className="updated-at">
    <span role="status">{stale ? 'Sin actualizar desde ' : 'Actualizado a las '}{formatTime(updatedAt)}</span>
    <button type="button" className="icon-button" onClick={onRefresh} disabled={refreshing} aria-label="Actualizar ahora"><Icon name="refresh" /></button>
  </div>
}
