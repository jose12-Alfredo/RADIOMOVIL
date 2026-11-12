---
name: RadioMovil
description: Acceso y espacio de trabajo empresarial sobrio — fases 8.4 a 8.6.
colors:
  surface: "#ffffff"
  canvas: "#f4f6f8"
  ink: "#17212f"
  muted: "#526071"
  action: "#184f9d"
  action-hover: "#123c77"
  intro: "#123551"
  brand: "#13355a"
  control-border: "#8a98a9"
  divider: "#e1e6ec"
  danger: "#a52424"
  notice-bg: "#f1f5fa"
  notice-ink: "#34485e"
  error-bg: "#fff0ef"
  error-ink: "#922222"
  disabled-bg: "#e6ebf1"
  subtle-hover: "#edf3fa"
  focus: "#2971cb"
typography:
  display:
    fontFamily: "'Segoe UI', system-ui, sans-serif"
    fontSize: "clamp(36px, 3.7vw, 68px)"
    fontWeight: 650
    lineHeight: 1.14
    letterSpacing: "-.035em"
  title:
    fontFamily: "'Segoe UI', system-ui, sans-serif"
    fontSize: "30px"
    fontWeight: 650
    lineHeight: 1.25
    letterSpacing: "-.025em"
  body:
    fontFamily: "'Segoe UI', system-ui, sans-serif"
    lineHeight: 1.6
  label:
    fontFamily: "'Segoe UI', system-ui, sans-serif"
    fontSize: "14px"
    fontWeight: 600
  button:
    fontFamily: "'Segoe UI', system-ui, sans-serif"
    fontSize: "15px"
    fontWeight: 600
rounded:
  control: "0.375rem"
  toggle: "4px"
spacing:
  touch: "2.75rem"
  field: "22px"
  section: "24px"
  heading: "32px"
components:
  button-primary:
    backgroundColor: "{colors.action}"
    textColor: "{colors.surface}"
    typography: "{typography.button}"
    rounded: "{rounded.control}"
    padding: "13px 18px"
    width: "100%"
  button-primary-hover:
    backgroundColor: "{colors.action-hover}"
  button-primary-disabled:
    backgroundColor: "{colors.disabled-bg}"
    textColor: "{colors.muted}"
  button-secondary:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.action}"
    typography: "{typography.button}"
    rounded: "{rounded.control}"
    padding: "13px 18px"
    width: "100%"
  password-toggle:
    textColor: "{colors.action}"
    rounded: "{rounded.toggle}"
    padding: "0 12px"
  input:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
    padding: "12px 14px"
    height: "50px"
    width: "100%"
  notice:
    backgroundColor: "{colors.notice-bg}"
    textColor: "{colors.notice-ink}"
    rounded: "{rounded.control}"
    padding: "14px 16px"
  notice-error:
    backgroundColor: "{colors.error-bg}"
    textColor: "{colors.error-ink}"
---

# Design System: RadioMovil

## Overview

**Creative North Star: "Acceso empresarial sobrio"**

Dirección aprobada: legibilidad, precisión y una tarea principal clara. La identidad se apoya en azul marino, blanco de trabajo y tipografía de interfaz; el carácter proviene de la composición y del monograma geométrico, sin decoración excesiva.

Cubre la autenticación (fase 8.4) y el espacio de trabajo (fase 8.6, en su propia sección). Fuentes de autenticación: `src/index.css`, `src/app/tokens.css`, `src/features/auth/AuthFrame.tsx` y `LoginPage.tsx`; contrato de superficie en `.impeccable/surfaces/auth.md`. El frontmatter registra valores reales, incluidos literales CSS; no todos son variables. Los tokens base `line`, `success` y `warning` no tienen uso visual demostrado aquí. Las rampas del sidecar son muestras OKLCH sintetizadas para el panel, no nuevos tokens de aplicación.

**Key Characteristics:**
- Composición plana y contraste entre bienvenida y trabajo.
- Controles legibles, etiquetas persistentes y foco visible.
- Marca tipográfica e iconos SVG geométricos, sin recursos raster de interfaz.

Evidencia: capturas offline revisadas a 1440, 820 y 390 px, con configuración pendiente. Se comunicaron 11 pruebas unitarias y 4 pruebas Edge aprobadas, ausencia de overflow a 320 px y detector `[]`; esta tarea documental no las ejecutó. El reviewer consideró aceptable el layout y señaló el aviso de logout, ya corregido en código mediante `messageTone: info` y `role="status"`. Esto no acredita login real, HTTPS, estados autenticados probados ni certificación general de accesibilidad.

## Colors

### Primary
Azul de acción (`action`) para la acción principal y controles textuales; `action-hover` intensifica la respuesta. Marino (`intro`) sostiene la bienvenida; `brand` identifica RadioMovil. No se usan gradientes.

### Neutral
Blanco (`surface`) en cabecera, campos y panel de trabajo; gris frío (`canvas`) en fondo y pie. `ink` jerarquiza el contenido y `muted` acompaña ayudas. El borde de campo es `control-border`, distinto del divisor tenue `divider` y del token base `line`.

Los avisos informativos usan `notice-bg`/`notice-ink`; los errores, `error-bg`/`error-ink`, con `danger` en validación de campos. La acción deshabilitada usa `disabled-bg` y texto atenuado. `focus` corresponde al contorno de foco.

**The Estado explícito Rule.** Cada estado se comunica con texto y semántica; cerrar sesión correctamente es información, no error.

## Typography

Una sola familia de sistema, sin descarga de fuentes. El cuerpo conserva el tamaño del navegador (normalmente 16 px); no hay una escala modular impuesta.

- **Display:** bienvenida fluida según frontmatter, siempre en dos líneas; a ≤1000 px `clamp(30px, 4.4vw, 38px)`, a ≤760 px 28 px con interlínea 1.2 y en teléfono bajo 22 px.
- **Title:** título de formulario; a ≤760 px pasa a 26 px y en ventana muy baja a 22 px.
- **Body:** bienvenida de 18 a 21 px/1.7 y máximo 35ch; en móvil 14 px/1.6 y 48ch. Introducción del formulario con interlínea 1.6.
- **Label:** etiquetas persistentes; ayudas y avisos de 13 px, pies de 12–13 px.
- **Marca:** 24 px, peso 750 para Radio y 450 para Movil, tracking −0.7 px; 22 px en móvil.

## Layout

Página flexible de altura mínima `100svh`, sin altura mínima fija en la cuadrícula: la página cabe en la ventana cuando su contenido cabe. Un solo margen lateral `--gutter` (`clamp(24px, 5vw, 80px)`, 24 px en una columna) alinea cabecera, bienvenida y pie, y respeta las zonas seguras. Cabecera `clamp(64px, 10vh, 96px)`; márgenes verticales ligados a la altura de la ventana. Bienvenida con texto limitado a 620 px; formulario centrado de hasta 408 px, sin tarjeta flotante.

- **Escritorio >1000 px:** dos columnas iguales; titular hasta 68 px en pantallas muy grandes.
- **Tablet 761–1000 px:** mantiene dos columnas, con el titular en dos líneas y el formulario dentro del margen de la cabecera.
- **Escritorio bajo (≥761 px y ≤640 px de alto):** se retiran nota secundaria y pie de bienvenida.
- **Una columna ≤760 px:** bienvenida compacta arriba y formulario alineado a la izquierda, hasta 480 px. Se ocultan descriptor de cabecera, nota secundaria y pie de bienvenida, no campos ni acciones.
- **Teléfono bajo (≤760 px y ≤700 px de alto):** banda solo con el titular y espaciado reducido; la acción principal queda a la vista.
- **Horizontal o ventana muy baja (≥561 px y ≤500 px de alto):** sin bienvenida ni pie general; campos y datos de sesión en dos columnas, y acción a todo el ancho.

Ritmo observado: 22 px entre campos, 24 px tras avisos, 32 px tras encabezado (24 px en móvil); se reducen en alturas bajas. Los controles interactivos miden al menos 44 px. Los textos de estado admiten salto de palabras largas. El cuerpo tiene mínimo técnico de 280 px.

Evidencia responsive: `/login` y `/session` medidas en Edge en 15 tamaños de 320×568 a 2560×1440, sin overflow horizontal y con la acción principal visible. El aspecto de `/session` se comprobó con una respuesta local de medición; el flujo real se valida aparte. Sin pruebas en dispositivos físicos.

## Espacio de trabajo (Fase 8.6)

Extensión Fase A: el riel de registro consulta las centrales activas de la empresa y muestra sus nombres, con selección explícita cuando hay varias. El origen sigue siendo el campo principal; teléfono, pasajero, destino y observaciones se agrupan en «Datos adicionales (opcionales)» para conservar rapidez de atención. Los controles comparten borde, foco y altura de los campos existentes. Sin central activa se informa el bloqueo; la posición exacta queda como «Pendiente de ubicar» si no se recibió coordenada.

Modo Operate: escala tipográfica fija en rem, misma familia y colores semánticos en `src/app/tokens.css`; estilos en `src/app/workspace.css`. Contrato en `.impeccable/surfaces`.

- **Armazón:** sidebar marina `intro` de 14rem con opción activa en blanco; en iconos de 1024 a 1279 px; menú en `<dialog>` por debajo. Topbar blanca de 3.75rem con título, cuenta y salida.
- **Consola:** panel de registro fijo de 20rem junto al trabajo, montado por encima de las rutas para conservar borrador, confirmación y envío dudoso; en Cuenta solo se oculta. De 1023 px hacia abajo, banda sobre la cola; en teléfono horizontal, una sola línea.
- **Estados:** insignia con punto y texto: Pendiente ámbar, Ofertada azul, Asignada marino, En viaje verde, Finalizada gris y Cancelada rojo. El color nunca va sin texto.
- **Firma:** la fila recién confirmada queda iluminada con fondo `subtle-hover` y borde de acción hasta el siguiente registro; si por orden cae en otra página, se fija sobre la lista con el rótulo «Recién registrada».
- **Avisos:** los informativos del espacio de trabajo van sobre `surface` con borde `divider`, porque `notice-bg` no se distingue sobre `canvas`.
- **Cola:** filas en cuadrícula con roles ARIA de tabla; bajo 34rem de contenedor, tarjetas de orden, estado y hora sobre el origen.
- **Panel:** banda continua de seis estados con cifras enlazadas al filtro, en lugar de tarjetas de métricas.
- **Flota (Fase B):** pestañas subrayadas en acción (Conductores, Vehículos, Historial de turnos); banda de tres estados del servidor (Disponible verde, Ocupado azul, Fuera de servicio gris) que también filtra, con relleno `#e6eefa`, línea inferior de acción y el texto «Filtro activo»; placa como chip con borde `line`, fondo `canvas` y cifras tabulares; abrir y cerrar turno en línea dentro de la fila, sin modal; un campo bloqueado por resultado dudoso usa fondo `canvas` y texto atenuado. Nota GPS explícita: «Disponible» significa turno abierto, no conexión. Títulos de topbar de una línea con elipsis en teléfono.
- **Asignación directa (2026-11-10):** la central asigna, el conductor no acepta. Registro: bloque «Ubicación en el mapa» con coordenadas por punto y botón «Abrir mapa grande» (ver selector amplio abajo). Recogida = cuadrado `intro` relleno; destino = cuadrado blanco con borde `intro`; conductores = círculo (verde reciente, hueco ámbar desactualizado); seleccionado = etiqueta en `action`. En el mapa de asignación recogida/destino van sin texto, con leyenda debajo, y los conductores por encima. Candidatos como filas radio completas (área táctil = fila), seleccionada con fondo `#e6eefa` y contorno de 1 px `action` (nunca borde lateral grueso). Botón primario nombra conductor y placa. Despacho: listas «Por asignar» y «Carreras activas» con insignias de estado existentes.
- **Retroalimentación:** bloques de carga, avisos de error, advertencia, éxito e información; los datos previos se conservan cuando falla un refresco, y el fallo se dice.

## Elevation & Depth

Sin sombras, desenfoques ni capas elevadas. La profundidad depende de bloques tonales y divisores de 1 px. No hay transiciones o animaciones decorativas; la regla de movimiento reducido fuerza desplazamiento automático.

## Shapes

Controles y avisos de esquinas discretamente redondeadas (`control`); botón Mostrar/Ocultar con radio `toggle`. Campos y botones con borde de 1 px. Marca R geométrica en SVG de 36 px dentro de un cuadrado redondeado; flecha de 20 px y candado de 18 px. Sin fotografías ni ilustraciones raster.

## Components

- **Botón primario:** ancho completo y altura mínima 50 px, flecha a la derecha. Hover solo habilitado; deshabilitado ante configuración pendiente, envío o sesión incierta. Durante envío muestra «Verificando acceso…».
- **Botón secundario:** blanco con borde de campo y texto de acción; hover `subtle-hover`. Usado para comprobar sesión y acciones de estado.
- **Campos:** altura 50 px; hover oscurece el borde, error lo cambia a `danger` y añade texto asociado mediante `aria-describedby`. Deshabilitados con fondo `canvas`; etiquetas externas y autocompletado explícito.
- **Contraseña:** reserva derecha de 88 px para Mostrar/Ocultar; control de altura mínima 44 px, `aria-pressed` y nombre accesible actualizado.
- **Avisos:** información con `role="status"`; fallo con `role="alert"`. Configuración pendiente encima de los campos. La confirmación de logout utiliza el tono informativo, no el rojo de error.
- **Foco y acceso:** contorno de 3 px con separación de 4 px; enlace «Saltar al contenido» visible al recibir foco; validación enfoca el primer campo incorrecto. Son mecanismos implementados, no una auditoría completa.
- **Contenedores:** formulario plano; detalles de sesión definidos en CSS como filas separadas por divisores y texto adaptable.

## Do's and Don'ts

### Do:
- **Do** Mantener la dirección sobria, la jerarquía tipográfica y el espacio de trabajo blanco.
- **Do** Conservar etiquetas, foco visible, estados textuales y acciones utilizables en móvil.
- **Do** Separar evidencia offline de autenticación real y confirmar datos operativos con el backend.

### Don't:
- **Don't** Añadir sombras, gradientes, raster o animación decorativa a esta dirección aprobada.
- **Don't** Presentar el cierre de sesión correcto como error.
- **Don't** Convertir planes de navegación o métricas simuladas en componentes ya implementados.
- **Selector amplio de ubicaciones (2026-11-10):** `<dialog>` modal (foco protegido, Escape con cambios pide confirmar). Escritorio ≥64rem: 85vw × 86dvh; columna izquierda (22rem, 19rem bajo 80rem) con encabezado, puntos como radios grandes (activo: contorno 1px+inset `intro`, fondo `#eef3f8`), campo «Pegar enlace o coordenadas» + «Ubicar», ayuda y pie fijo «Cancelar | Confirmar ubicación»; el mapa ocupa todo el alto a la derecha con insignia «Tocar coloca: X» (recogida en `intro` relleno, destino blanco). Tablet: 94vw × 92dvh. Teléfono vertical (<48rem): pantalla completa, controles arriba (puntos en 2 columnas), mapa al centro, pie fijo con área segura. Teléfono horizontal (alto <33rem): pantalla completa con panel y mapa lado a lado. Marcadores del selector `map-pin` de 1.375rem, arrastrables, activo con anillo `action`. Zoom de MapLibre a 44 px (regla fuera de capa: su CSS no tiene capa). Controles ≥44 px.

