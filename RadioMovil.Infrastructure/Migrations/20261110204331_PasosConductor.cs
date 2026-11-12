using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace RadioMovil.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PasosConductor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_viajes_ConductorId",
                table: "viajes");

            migrationBuilder.DropIndex(
                name: "IX_viajes_SolicitudId",
                table: "viajes");

            migrationBuilder.CreateTable(
                name: "viaje_eventos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViajeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    En = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Posicion = table.Column<Point>(type: "geometry(Point,4326)", nullable: true),
                    PrecisionMetros = table.Column<double>(type: "double precision", nullable: true),
                    GpsRegistradoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Observaciones = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_viaje_eventos", x => x.Id);
                    table.CheckConstraint("CK_viaje_evento_tipo", "\"Tipo\" IN ('EnCamino', 'EnPuerta', 'EnCurso', 'Finalizado')");
                    table.ForeignKey(
                        name: "FK_viaje_eventos_viajes_ViajeId",
                        column: x => x.ViajeId,
                        principalTable: "viajes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_viajes_ConductorId",
                table: "viajes",
                column: "ConductorId",
                unique: true,
                filter: "\"Estado\" IN ('Asignado', 'EnCamino', 'EnPuerta', 'EnCurso')");

            migrationBuilder.CreateIndex(
                name: "IX_viajes_SolicitudId",
                table: "viajes",
                column: "SolicitudId",
                unique: true,
                filter: "\"Estado\" IN ('Asignado', 'EnCamino', 'EnPuerta', 'EnCurso')");

            migrationBuilder.CreateIndex(
                name: "IX_viaje_eventos_ViajeId_Tipo",
                table: "viaje_eventos",
                columns: new[] { "ViajeId", "Tipo" },
                unique: true);

            // Los pasos del conductor son estados ACTIVOS: la exclusividad por conductor y por solicitud los incluye.
            migrationBuilder.Sql(FuncionPasosConductor);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(FuncionAsignacionDirecta);
            migrationBuilder.DropTable(
                name: "viaje_eventos");

            migrationBuilder.DropIndex(
                name: "IX_viajes_ConductorId",
                table: "viajes");

            migrationBuilder.DropIndex(
                name: "IX_viajes_SolicitudId",
                table: "viajes");

            migrationBuilder.CreateIndex(
                name: "IX_viajes_ConductorId",
                table: "viajes",
                column: "ConductorId",
                unique: true,
                filter: "\"Estado\" IN ('Asignado', 'EnCurso')");

            migrationBuilder.CreateIndex(
                name: "IX_viajes_SolicitudId",
                table: "viajes",
                column: "SolicitudId",
                unique: true,
                filter: "\"Estado\" IN ('Asignado', 'EnCurso')");
        }
    
        private const string FuncionPasosConductor = """
            CREATE OR REPLACE FUNCTION validar_exclusividad_despacho() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                IF TG_TABLE_NAME = 'intentos_despacho' AND NEW."Estado" <> 'Ofertado' THEN RETURN NEW; END IF;
                IF TG_TABLE_NAME = 'viajes' AND NEW."Estado" NOT IN ('Asignado', 'EnCamino', 'EnPuerta', 'EnCurso') THEN RETURN NEW; END IF;
                PERFORM pg_advisory_xact_lock(hashtextextended(NEW."ConductorId"::text, 42));
                PERFORM pg_advisory_xact_lock(hashtextextended(NEW."SolicitudId"::text, 43));
                IF TG_OP = 'INSERT' AND NOT EXISTS (SELECT 1 FROM turnos t WHERE t."ConductorId" = NEW."ConductorId"
                    AND t."EmpresaId" = NEW."EmpresaId" AND t."Inicio" <= clock_timestamp()
                    AND (t."Fin" IS NULL OR t."Fin" > clock_timestamp())) THEN
                    RAISE EXCEPTION 'Asignación u oferta sin turno abierto del conductor' USING ERRCODE = '23514';
                END IF;
                IF TG_TABLE_NAME = 'intentos_despacho' THEN
                    IF EXISTS (SELECT 1 FROM viajes v WHERE v."ConductorId" = NEW."ConductorId"
                        AND v."Estado" IN ('Asignado','EnCamino','EnPuerta','EnCurso'))
                       OR EXISTS (SELECT 1 FROM viajes v WHERE v."SolicitudId" = NEW."SolicitudId"
                        AND v."Estado" IN ('Asignado','EnCamino','EnPuerta','EnCurso')) THEN
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

        // Copia exacta de la función desplegada por AsignacionDirecta (restaurada por Down).
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
    }
}
