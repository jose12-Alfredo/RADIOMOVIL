import { useAuth } from '../auth/auth-context'

/** Solo oculta acciones; la autorización real la valida siempre el backend. */
export function useIsAdmin(): boolean {
  const { state } = useAuth()
  return state.session?.roles.includes('Administrador') ?? false
}
