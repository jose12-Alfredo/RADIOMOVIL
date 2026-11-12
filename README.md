# RadioMovil

Despacho manual asistido de radiotaxis: GPS de la app de conductores → API ASP.NET Core → mapa React, sugerencia por proximidad y asignación directa por la central. PostgreSQL/PostGIS (Neon) es la fuente de verdad.

## Estructura

| Carpeta | Contenido |
|---|---|
| `RADIOMOVIL/` | API ASP.NET Core (host) |
| `RadioMovil.Domain/`, `RadioMovil.Application/`, `RadioMovil.Infrastructure/`, `RadioMovil.Workers/` | Capas del backend (.NET, EF Core + Npgsql) |
| `RadioMovil.UnitTests/` | Pruebas del backend |
| `frontend-web/` | Frontend React + Vite + TypeScript |
| `radiomovil_conductor/` | App móvil de conductores (Flutter) |

## Configuración

1. Copiar `.env.example` a `.env.local` (raíz) y `frontend-web/.env.example` a `frontend-web/.env.local`, y completar los valores.
2. Los archivos `.env.local` y certificados **no** se versionan.

## Ejecutar

Backend:

```bash
dotnet build RADIOMOVIL.slnx
dotnet run --project RADIOMOVIL
```

Frontend:

```bash
cd frontend-web
npm install
npm run dev
```

Reglas del proyecto: ver `AGENTS.md`.
