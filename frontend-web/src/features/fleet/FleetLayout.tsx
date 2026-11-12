import { NavLink, Outlet } from 'react-router-dom'

const tabs = [
  { to: '/flota', label: 'Conductores', end: true },
  { to: '/flota/vehiculos', label: 'Vehículos', end: false },
  { to: '/flota/ubicaciones', label: 'Ubicaciones', end: false },
  { to: '/flota/turnos', label: 'Historial de turnos', end: false },
]

export function FleetLayout() {
  return <div className="fleet">
    <nav className="fleet-tabs" aria-label="Secciones de flota">
      {tabs.map(tab => <NavLink key={tab.to} to={tab.to} end={tab.end}>{tab.label}</NavLink>)}
    </nav>
    <Outlet />
  </div>
}
