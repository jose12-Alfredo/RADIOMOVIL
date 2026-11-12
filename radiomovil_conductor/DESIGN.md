---
name: RadioMovil Conductor
description: App Android de conductores — mapa a pantalla completa y panel de trabajo.
extends: ../frontend-web/DESIGN.md
platform: android (Material 3)
colors:
  primary: "#184f9d"        # action (web)
  brand: "#13355a"
  surface: "#ffffff"
  surface-container: "#f4f6f8" # canvas (web)
  on-surface: "#17212f"     # ink
  on-surface-variant: "#526071" # muted
  outline: "#8a98a9"
  outline-variant: "#e1e6ec"
  error: "#a52424"
  ok: "#1e6b3a"
  warn: "#8a5a00"
  dark-surface: "#0f1620"
  dark-primary: "#a9c7ff"
---

# Design System: RadioMovil Conductor

Fuente: `lib/app/theme.dart` (roles Material 3 + extensión `RmStatusColors`), `lib/app/widgets.dart`.

## Mundo
Hereda el mundo “Acceso empresarial sobrio” de la web: azul de acción, marino de marca, neutros fríos, monograma RM geométrico sin raster. Se expresa mediante roles de Material 3; tema claro y oscuro de primera clase (turnos de día y de noche) siguiendo el sistema.

## Estructura (modo Operate, género app de conductor)
- Mapa MapLibre a pantalla completa; solo la posición real del teléfono. Sin permiso: vista mundial, nunca un punto inventado.
- Arriba: botón circular de perfil (abre NavigationDrawer) y chip de conexión.
- Abajo: panel de trabajo con esquinas superiores de 24 dp, máximo 62 % del alto, desplazable.
- Una acción principal por momento: “Iniciar turno” (Filled) o “Finalizar turno” (Outlined en color error, con confirmación).
- Botón pequeño “Centrar” solo cuando el usuario dejó de seguir su posición.

## Movimiento firma
**Banda de estado**: bloque de 16 dp de radio con color de estado + icono 32 dp + estado en titleLarge + detalle. Disponible = ok, Ocupado = warn, Fuera de servicio = neutro. Legible de reojo desde el soporte del vehículo y anunciada como región viva.

## Carrera asignada
- Con carrera, el panel la muestra arriba en lugar de la banda de estado: encabezado (recién llegada: bloque `primary` con notifications_active, "Nueva carrera asignada", 1 min; luego "Carrera asignada"/"Viaje en curso"), bloque `surfaceContainerHigh` con Recogida → Destino unidos por un conector, pasajero e indicaciones.
- Acción principal: "Cómo llegar a la recogida/al destino" (Filled) solo con coordenadas; si no, aviso info. "Llamar al pasajero" (Tonal) solo con teléfono. Nunca aceptar/rechazar.
- Mapa: recogida = círculo `primary` 11 dp con borde `surface`; destino = círculo `onSurface` 9 dp. Encuadre automático una vez por carrera/estado.

## Estados
- GPS: Reciente (gps_fixed, ok), Desactualizado (gps_not_fixed, warn), Sin ubicación (gps_off, error) + “Última ubicación enviada hace N s · precisión ±M m”.
- Conexión: “Conectado” (ok) / “Sin conexión · Reintentar” (error).
- Avisos `Notice` info/warn/error con icono, siempre texto explícito.

## Reglas
- Objetivos táctiles ≥48 dp; botones principales 56 dp de alto.
- Tipografía: escala Material (Roboto, sp); títulos w700, etiquetas w600.
- Color nunca es la única señal: siempre icono y texto.
- Sin datos simulados: solo lo que registró la central.

## Pendiente de verificar
Capturas en teléfono real (claro/oscuro, escala de fuente 1.3) con `adb exec-out screencap`. Hasta ahora solo hay prueba de widget a 320×568 sin desbordes en login.
