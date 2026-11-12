import { useState } from 'react'
import { Outlet, useMatches } from 'react-router-dom'
import { RegisterPanel } from './RegisterPanel'
import type { ConsoleContext } from './console-context'
import type { ServiceRequest } from './requests'

/**
 * El registro vive por encima de las rutas: navegar nunca pierde el borrador, la confirmación
 * ni la clave de un envío dudoso. En Cuenta solo se oculta, sin desmontarse.
 */
export function ConsoleLayout() {
  const [lastRegistered, setLastRegistered] = useState<ServiceRequest | null>(null)
  const matches = useMatches()
  const hidePanel = (matches.at(-1)?.handle as { hideRegister?: boolean } | undefined)?.hideRegister === true
  const context: ConsoleContext = { lastRegistered }
  return <div className={hidePanel ? 'console console-full' : 'console'}>
    <div className="register-rail" hidden={hidePanel}><RegisterPanel onRegistered={setLastRegistered} /></div>
    <div className="console-work"><Outlet context={context} /></div>
  </div>
}
