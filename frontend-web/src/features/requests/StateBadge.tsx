import { requestStateLabel } from './requests'
import type { RequestState } from './requests'

export function StateBadge({ state }: { state: RequestState }) {
  return <span className={`state-badge state-${state.toLowerCase()}`}>{requestStateLabel[state]}</span>
}
