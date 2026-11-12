using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace RadioMovil.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AsignacionDirecta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecibidaEn",
                table: "viajes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Point>(
                name: "PuntoDestino",
                table: "solicitudes",
                type: "geometry(Point,4326)",
                nullable: true);

            // Asignación directa: crear un viaje activo exige ahora turno abierto del conductor en la misma empresa,
            // igual que una oferta, tras el MISMO advisory lock por conductor que toma el cierre de turno.
            migrationBuilder.Sql(FuncionAsignacionDirecta);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(FuncionFaseB);
            migrationBuilder.DropColumn(
                name: "RecibidaEn",
                table: "viajes");

            migrationBuilder.DropColumn(
                name: "PuntoDestino",
                table: "solicitudes");
        }

        private const string FuncionAsignacionDirecta = """
            CREATE OR REPLACE FUNCTION validar_exclusividad_despacho() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                IF TG_TABLE_NAME = 'intentos_despacho' AND NEW."Estado" <> 'Ofertado' THEN RETURN NEW; END IF;
                IF TG_TABLE_NAME = 'viajes' AND NEW."Estado" NOT IN ('Asignado', 'EnCurso') THEN RETURN NEW; END IF;
                PERFORM pg_advisory_xact_lock(hashtextextended(NEW."ConductorId"::text, 42));
                PERFORM pg_advisory_xact_lock(hashtextextended(NEW."SolicitudId"::text, 43));
                IF TG_OP = 'INSERT' AND NOT EXISTS (SELECT 1 FROM turnos t WHERE t."ConductorId" = NEW."ConductorId"
                    AND t."EmpresaId" = NEW."EmpresaId" AND t."Inicio" <= clock_timestamp()
                    AND (t."Fin" IS NULL OR t."Fin" > clock_timestamp())) THEN
                    RAISE EXCEPTION 'Asignación u oferta sin turno abierto del conductor' USING ERRCODE = '23514';
                END IF;
                IF TG_TABLE_NAME = 'intentos_despacho' THEN
                    IF EXISTS (SELECT 1 FROM viajes v WHERE v."ConductorId" = NEW."ConductorId"
                        AND v."Estado" IN ('Asignado','EnCurso'))
                       OR EXISTS (SELECT 1 FROM viajes v WHERE v."SolicitudId" = NEW."SolicitudId"
                        AND v."Estado" IN ('Asignado','EnCurso')) THEN
                        RAISE EXCEPTION 'Oferta incompatible con viaje activo' USING ERRCODE = '23514';
                    END IF;
                ELSE
                    IF EXISTS (SELECT 1 FROM intentos_despacho i WHERE i."ConductorId" = NEW."ConductorId"
                        AND i."Estado" = 'Ofertado')
                       OR EXISTS (SELECT 1 FROM intentos_despacho i WHERE i."SolicitudId" = NEW."SolicitudId"
                        AND i."Estado" = 'Ofertado') THEN
                        RAISE EXCEPTION 'Viaje incompatible con oferta activa' USING ERRCODE = '23514';
                    END IF;
                END IF;
                RETURN NEW;
            END;
            $body$;
            """;

        // Copia exacta de la función desplegada por FaseBFlotaTurnos (restaurada por Down).
        private const string FuncionFaseB = """
            CREATE OR REPLACE FUNCTION validar_exclusividad_despacho() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                IF TG_TABLE_NAME = 'intentos_despacho' AND NEW."Estado" <> 'Ofertado' THEN RETURN NEW; END IF;
                IF TG_TABLE_NAME = 'viajes' AND NEW."Estado" NOT IN ('Asignado', 'EnCurso') THEN RETURN NEW; END IF;
                PERFORM pg_advisory_xact_lock(hashtextextended(NEW."ConductorId"::text, 42));
                PERFORM pg_advisory_xact_lock(hashtextextended(NEW."SolicitudId"::text, 43));
                IF TG_TABLE_NAME = 'intentos_despacho' THEN
                    IF TG_OP = 'INSERT' AND NOT EXISTS (SELECT 1 FROM turnos t WHERE t."ConductorId" = NEW."ConductorId"
                        AND t."EmpresaId" = NEW."EmpresaId" AND t."Inicio" <= clock_timestamp()
                        AND (t."Fin" IS NULL OR t."Fin" > clock_timestamp())) THEN
                        RAISE EXCEPTION 'Oferta sin turno abierto del conductor' USING ERRCODE = '23514';
                    END IF;
                    IF EXISTS (SELECT 1 FROM viajes v WHERE v."ConductorId" = NEW."ConductorId"
                        AND v."Estado" IN ('Asignado','EnCurso'))
                       OR EXISTS (SELECT 1 FROM viajes v WHERE v."SolicitudId" = NEW."SolicitudId"
                        AND v."Estado" IN ('Asignado','EnCurso')) THEN
                        RAISE EXCEPTION 'Oferta incompatible con viaje activo' USING ERRCODE = '23514';
                    END IF;
                ELSE
                    IF EXISTS (SELECT 1 FROM intentos_despacho i WHERE i."ConductorId" = NEW."ConductorId"
                        AND i."Estado" = 'Ofertado')
                       OR EXISTS (SELECT 1 FROM intentos_despacho i WHERE i."SolicitudId" = NEW."SolicitudId"
                        AND i."Estado" = 'Ofertado') THEN
                        RAISE EXCEPTION 'Viaje incompatible con oferta activa' USING ERRCODE = '23514';
                    END IF;
                END IF;
                RETURN NEW;
            END;
            $body$;
            """;
    }
}
