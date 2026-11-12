# Product

<!-- impeccable:product-schema 1 -->

## Platform

android

## Stack

Flutter + Dart (decisión permanente del responsable, 2026-11-09). Material 3. Mapa con MapLibre (`maplibre_gl`). Backend exclusivo: API ASP.NET Core 10 de RadioMovil (Neon Testing).

## Users

Conductores de radiomóviles de una empresa cliente de RadioMovil. Trabajan en turnos de día y de noche, con el teléfono en el soporte del vehículo. Su trabajo: iniciar turno con un vehículo autorizado, compartir su ubicación real mientras trabajan y, en una fase posterior, recibir las carreras que la central le asigna directamente (sin aceptar ni rechazar), ver la ubicación del pasajero y seguir rutas.

## Product Purpose

Que la central vea al conductor en el mapa con ubicación real y estado fiable, para sugerirlo por proximidad y despacharle carreras. Éxito: el conductor entiende de un vistazo si está en turno, si su ubicación llega a la central y qué debe hacer.

## Positioning

App de conductor de una central telefónica de radiomóviles (despacho manual asistido), no un marketplace de pasajeros. El estado del conductor y el GPS los decide el servidor; la app nunca los inventa.

## Operating Context

- Uso dentro del vehículo, de reojo, con luz solar o de noche: tema claro y oscuro del sistema.
- Teléfonos Android mixtos, incluida gama baja, pantallas pequeñas y datos móviles inestables.
- El responsable pidió una experiencia dinámica y fácil de entender, del género de las apps de conductores de transporte (mapa a pantalla completa, panel inferior, acciones grandes). Es una referencia de género, no de marca: no copiar identidad, colores ni recursos de terceros.

## Capabilities and Constraints

- v1: login empresarial Bearer + refresh de un solo uso, perfil y vehículo, turno propio (abrir/cerrar con Idempotency-Key), estado calculado por el servidor, permisos de ubicación, envío periódico de GPS real durante el turno, estado de conexión y última ubicación enviada.
- Disponibilidad: el servidor la calcula (Disponible con turno abierto, Ocupado con oferta/viaje, FueraDeServicio sin turno); no existe contrato para cambiarla manualmente.
- Asignación directa (decisión 2026-11-10): la central asigna; la app muestra la carrera asignada, sin botones de aceptar/rechazar. Contrato publicado en Testing (2026-11-10) e implementado: orden con origen/destino/pasajero/indicaciones, aviso con sonido y vibración, acuse de entrega, Cómo llegar y Llamar. No mostrar datos simulados.
- Segundo plano: no prometido hasta implementar servicio en primer plano con notificación persistente y probarlo en un teléfono real.
- HTTPS obligatorio con validación de certificados; nunca localhost desde un teléfono; nunca conexión directa a la base de datos.

## Evidence on Hand

Sin capturas de dispositivo real todavía. No existen datos reales de pasajeros ni rutas en la app.

## Product Principles

- El estado se lee en un segundo: turno, conexión y GPS siempre visibles.
- Una acción principal por momento, grande y alcanzable con el pulgar.
- La verdad viene del servidor; la app muestra lo confirmado y dice cuando algo está desactualizado.
- Resistente a mala cobertura: reintenta sin duplicar ni falsear.

## Accessibility & Inclusion

Objetivos táctiles ≥48 dp, texto en sp que respeta la escala de fuente del sistema, estados comprensibles sin depender solo del color, contraste AA en claro y oscuro.
