# RadioMovil — contexto confirmado

## Platform
web

## Users
Administradores y operadores de centrales de radiomóviles. El escritorio es prioritario para registrar solicitudes, consultar conductores y despachar rápidamente.

## Product Purpose
Una única aplicación React adaptable a escritorio, laptop, tablet y teléfono, sin perder funciones esenciales en pantallas pequeñas.

## Capabilities and Constraints
ASP.NET Core es la autoridad de negocio. Cookies HttpOnly/Secure y CSRF; integración exclusivamente Testing. No presentar datos simulados como operativos. Fase 8.6: panel, solicitudes, despacho en lectura y cuenta implementados; ofertas, mapas y datos telefónicos completos pendientes de backend. Decisión 2026-11-10: la central ASIGNA directamente la carrera («Asignar carrera») y ve la confirmación del backend; el conductor no acepta ni rechaza.

## Accessibility & Inclusion
Teclado, ratón y tacto. Diseño desktop-first, sidebar convertible en menú móvil, tablas mediante tarjetas o scroll contenido según el caso. Sin cortes, superposiciones ni scroll horizontal de página innecesario. Estados y acciones deben ser comprensibles sin depender solo del color.

## Product Principles
- Rapidez operativa y legibilidad.
- Integridad de datos y confirmaciones del servidor.
- Una aplicación adaptable, sin versión móvil separada.
- Impeccable obligatorio antes de crear interfaces.
