import { createContext, useContext } from 'react'
import type { Session } from './session'

export type AuthState = {
  status: 'checking' | 'anonymous' | 'authenticated' | 'denied' | 'uncertain'
  session: Session | null
  message: string
  messageTone?: 'info' | 'error'
}
export type AuthContextValue = {
  state: AuthState
  busy: boolean
  configured: boolean
  restore: () => Promise<void>
  /** Comprueba la sesión sin desmontar la aplicación ni perder lo escrito. */
  revalidate: () => Promise<void>
  login: (email: string, password: string) => Promise<void>
  logout: () => Promise<void>
}
export const AuthContext = createContext<AuthContextValue | null>(null)
export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) throw new Error('AuthProvider es obligatorio.')
  return context
}
