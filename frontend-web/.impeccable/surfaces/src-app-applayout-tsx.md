---
version: 1
slug: "src-app-applayout-tsx"
primary_target: "src/app/AppLayout.tsx"
related_targets: ["src/features/requests/RequestsPage.tsx","src/features/dashboard/DashboardPage.tsx"]
---

# Espacio de trabajo RadioMovil — modo Operate
Alcance: armazón administrativo, panel, solicitudes (registro, cola, filtros, paginación, detalle), despacho en solo lectura y cuenta. Sin mapas, ofertas ni datos ficticios.
Usuarios: administradores y operadores de central, atendiendo llamadas en escritorio. Tarea principal: registrar y seguir con la siguiente llamada.

## Direction contract
THESIS: el registro nunca se mueve y la cola es la referencia; se rechaza el panel de tarjetas de métricas como pantalla principal.
OWN-WORLD: riel marino, blanco de trabajo, azul de acción, estados con color y texto, cifras tabulares, tipografía de interfaz y monograma R.
STORY: el operador ve qué espera, escribe el origen, pulsa Enter, recibe el número de orden y ya está listo para la siguiente llamada.
FIRST VIEWPORT: sidebar marina a la izquierda, topbar con título y cuenta; panel de registro fijo de 20rem; a su derecha, la cola con filtros por estado y cabecera de orden. Laptop: sidebar en iconos. Tablet y móvil: menú, registro en banda y cola a todo el ancho. Firma: la fila recién confirmada queda iluminada hasta el siguiente registro.
FORM: Consola de llamada, posición 2 de la lista ordenada, sin desventajas de ancho por sidebar adaptable; seed:98dfce88fae26da3.
FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
