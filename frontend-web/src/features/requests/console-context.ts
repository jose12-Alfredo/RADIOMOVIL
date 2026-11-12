import { useOutletContext } from 'react-router-dom'
import type { ServiceRequest } from './requests'

export type ConsoleContext = { lastRegistered: ServiceRequest | null }

export function useConsole(): ConsoleContext {
  return useOutletContext<ConsoleContext>()
}
