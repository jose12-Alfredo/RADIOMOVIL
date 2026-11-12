import { Outlet } from 'react-router-dom'
import { AuthProvider } from '../features/auth/AuthProvider'

export function AuthRoot() { return <AuthProvider><Outlet /></AuthProvider> }
