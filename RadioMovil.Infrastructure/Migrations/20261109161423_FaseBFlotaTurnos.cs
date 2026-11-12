using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioMovil.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FaseBFlotaTurnos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AbiertoPor",
                table: "turnos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CerradoPor",
                table: "turnos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoCierre",
                table: "turnos",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "operaciones_flota",
                columns: table => new
                {
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ambito = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Clave = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Huella = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecursoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operaciones_flota", x => new { x.EmpresaId, x.Ambito, x.Clave });
                });

            migrationBuilder.CreateIndex(
                name: "IX_turnos_EmpresaId_Inicio",
                table: "turnos",
                columns: new[] { "EmpresaId", "Inicio" });

            // Misma función, una comprobación más: una oferta NUEVA exige turno abierto del
            // conductor. Se evalúa después del advisory lock por conductor, el mismo que toma
            // el cierre de turno, por lo que cierre y oferta concurrentes quedan serializados.
            migrationBuilder.Sql(FuncionConTurno);
        }

        private const string FuncionConTurno = """
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

        private const string FuncionAnterior = """
            CREATE OR REPLACE FUNCTION validar_exclusividad_despacho() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                IF TG_TABLE_NAME = 'intentos_despacho' AND NEW."Estado" <> 'Ofertado' THEN RETURN NEW; END IF;
                IF TG_TABLE_NAME = 'viajes' AND NEW."Estado" NOT IN ('Asignado', 'EnCurso') THEN RETURN NEW; END IF;
                PERFORM pg_advisory_xact_lock(hashtextextended(NEW."ConductorId"::text, 42));
                PERFORM pg_advisory_xact_lock(hashtextextended(NEW."SolicitudId"::text, 43));
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(FuncionAnterior);
            migrationBuilder.DropTable(
                name: "operaciones_flota");

            migrationBuilder.DropIndex(
                name: "IX_turnos_EmpresaId_Inicio",
                table: "turnos");

            migrationBuilder.DropColumn(
                name: "AbiertoPor",
                table: "turnos");

            migrationBuilder.DropColumn(
                name: "CerradoPor",
                table: "turnos");

            migrationBuilder.DropColumn(
                name: "MotivoCierre",
                table: "turnos");
        }
    }
}
