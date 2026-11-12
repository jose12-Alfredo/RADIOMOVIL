import { createBrowserRouter, Navigate } from 'react-router-dom'
import { AppLayout } from './AppLayout'
import { AuthRoot } from './AuthRoot'
import { AccountPage } from '../features/auth/AccountPage'
import { LoginPage } from '../features/auth/LoginPage'
import { ProtectedRoute } from '../features/auth/SessionPage'
import { DashboardPage } from '../features/dashboard/DashboardPage'
import { DispatchPage } from '../features/dispatch/DispatchPage'
import { DriverGpsPage } from '../features/driver/DriverGpsPage'
import { DriversPage } from '../features/fleet/DriversPage'
import { FleetLayout } from '../features/fleet/FleetLayout'
import { ShiftsPage } from '../features/fleet/ShiftsPage'
import { VehiclesPage } from '../features/fleet/VehiclesPage'
import { ConsoleLayout } from '../features/requests/ConsoleLayout'
import { RequestDetailPage } from '../features/requests/RequestDetailPage'
import { RequestsPage } from '../features/requests/RequestsPage'

export const router = createBrowserRouter([{ element: <AuthRoot />, children: [
  { path: '/', element: <Navigate to="/solicitudes" replace /> },
  { path: '/login', element: <LoginPage /> },
  { element: <ProtectedRoute roles={['Conductor']} />, children: [
    { path: '/conductor', element: <DriverGpsPage /> },
  ] },
  { element: <ProtectedRoute roles={['Administrador', 'Operador']} />, children: [
    { path: '/session', element: <Navigate to="/cuenta" replace /> },
    { element: <AppLayout />, children: [{ element: <ConsoleLayout />, children: [
      { path: '/panel', element: <DashboardPage />, handle: { title: 'Panel' } },
      { path: '/solicitudes', element: <RequestsPage />, handle: { title: 'Solicitudes' } },
      { path: '/solicitudes/:id', element: <RequestDetailPage />, handle: { title: 'Detalle de solicitud' } },
      { path: '/despacho', element: <DispatchPage />, handle: { title: 'Despacho' } },
      { path: '/flota', element: <FleetLayout />, children: [
        { index: true, element: <DriversPage />, handle: { title: 'Conductores' } },
        { path: 'vehiculos', element: <VehiclesPage />, handle: { title: 'Vehículos' } },
        { path: 'turnos', element: <ShiftsPage />, handle: { title: 'Turnos' } },
        // MapLibre pesa ~1 MB: solo se descarga al abrir Ubicaciones.
        { path: 'ubicaciones', lazy: async () => ({ Component: (await import('../features/fleet/LocationsPage')).LocationsPage }), handle: { title: 'Ubicaciones' } },
      ] },
      { path: '/cuenta', element: <AccountPage />, handle: { title: 'Cuenta', hideRegister: true } },
    ] }] },
  ] },
  { path: '*', element: <Navigate to="/login" replace /> },
] }])
