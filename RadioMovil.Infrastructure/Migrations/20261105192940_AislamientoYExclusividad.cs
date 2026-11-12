using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioMovil.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AislamientoYExclusividad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_intentos_despacho_conductores_ConductorId",
                table: "intentos_despacho");

            migrationBuilder.DropForeignKey(
                name: "FK_intentos_despacho_solicitudes_SolicitudId",
                table: "intentos_despacho");

            migrationBuilder.DropForeignKey(
                name: "FK_solicitud_eventos_solicitudes_SolicitudId",
                table: "solicitud_eventos");

            migrationBuilder.DropForeignKey(
                name: "FK_turnos_conductores_ConductorId",
                table: "turnos");

            migrationBuilder.DropForeignKey(
                name: "FK_turnos_vehiculos_VehiculoId",
                table: "turnos");

            migrationBuilder.DropForeignKey(
                name: "FK_viajes_conductores_ConductorId",
                table: "viajes");

            migrationBuilder.DropForeignKey(
                name: "FK_viajes_solicitudes_SolicitudId",
                table: "viajes");

            migrationBuilder.AddColumn<Guid>(
                name: "EmpresaId",
                table: "solicitud_eventos",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "EmpresaId",
                table: "intentos_despacho",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Las filas existentes heredan su empresa de la solicitud, no del valor temporal de la columna.
            migrationBuilder.Sql("UPDATE solicitud_eventos e SET \"EmpresaId\" = s.\"EmpresaId\" FROM solicitudes s WHERE e.\"SolicitudId\" = s.\"Id\"");
            migrationBuilder.Sql("UPDATE intentos_despacho i SET \"EmpresaId\" = s.\"EmpresaId\" FROM solicitudes s WHERE i.\"SolicitudId\" = s.\"Id\"");
            migrationBuilder.Sql("ALTER TABLE solicitud_eventos ALTER COLUMN \"EmpresaId\" DROP DEFAULT; ALTER TABLE intentos_despacho ALTER COLUMN \"EmpresaId\" DROP DEFAULT;");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_vehiculos_EmpresaId_Id",
                table: "vehiculos",
                columns: new[] { "EmpresaId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_solicitudes_EmpresaId_Id",
                table: "solicitudes",
                columns: new[] { "EmpresaId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_conductores_EmpresaId_Id",
                table: "conductores",
                columns: new[] { "EmpresaId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_viajes_EmpresaId_ConductorId",
                table: "viajes",
                columns: new[] { "EmpresaId", "ConductorId" });

            migrationBuilder.CreateIndex(
                name: "IX_viajes_EmpresaId_SolicitudId",
                table: "viajes",
                columns: new[] { "EmpresaId", "SolicitudId" });

            migrationBuilder.CreateIndex(
                name: "IX_turnos_EmpresaId_ConductorId",
                table: "turnos",
                columns: new[] { "EmpresaId", "ConductorId" });

            migrationBuilder.CreateIndex(
                name: "IX_turnos_EmpresaId_VehiculoId",
                table: "turnos",
                columns: new[] { "EmpresaId", "VehiculoId" });

            migrationBuilder.CreateIndex(
                name: "IX_solicitud_eventos_EmpresaId_SolicitudId",
                table: "solicitud_eventos",
                columns: new[] { "EmpresaId", "SolicitudId" });

            migrationBuilder.CreateIndex(
                name: "IX_intentos_despacho_EmpresaId_ConductorId",
                table: "intentos_despacho",
                columns: new[] { "EmpresaId", "ConductorId" });

            migrationBuilder.CreateIndex(
                name: "IX_intentos_despacho_EmpresaId_SolicitudId",
                table: "intentos_despacho",
                columns: new[] { "EmpresaId", "SolicitudId" });

            migrationBuilder.AddForeignKey(
                name: "FK_intentos_despacho_conductores_EmpresaId_ConductorId",
                table: "intentos_despacho",
                columns: new[] { "EmpresaId", "ConductorId" },
                principalTable: "conductores",
                principalColumns: new[] { "EmpresaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_intentos_despacho_solicitudes_EmpresaId_SolicitudId",
                table: "intentos_despacho",
                columns: new[] { "EmpresaId", "SolicitudId" },
                principalTable: "solicitudes",
                principalColumns: new[] { "EmpresaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_solicitud_eventos_solicitudes_EmpresaId_SolicitudId",
                table: "solicitud_eventos",
                columns: new[] { "EmpresaId", "SolicitudId" },
                principalTable: "solicitudes",
                principalColumns: new[] { "EmpresaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_turnos_conductores_EmpresaId_ConductorId",
                table: "turnos",
                columns: new[] { "EmpresaId", "ConductorId" },
                principalTable: "conductores",
                principalColumns: new[] { "EmpresaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_turnos_vehiculos_EmpresaId_VehiculoId",
                table: "turnos",
                columns: new[] { "EmpresaId", "VehiculoId" },
                principalTable: "vehiculos",
                principalColumns: new[] { "EmpresaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_viajes_conductores_EmpresaId_ConductorId",
                table: "viajes",
                columns: new[] { "EmpresaId", "ConductorId" },
                principalTable: "conductores",
                principalColumns: new[] { "EmpresaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_viajes_solicitudes_EmpresaId_SolicitudId",
                table: "viajes",
                columns: new[] { "EmpresaId", "SolicitudId" },
                principalTable: "solicitudes",
                principalColumns: new[] { "EmpresaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            // Índices parciales por tabla no cubren oferta y viaje simultáneos: serializar
            // por conductor y solicitud con locks transaccionales, comprobando la otra tabla.
            migrationBuilder.Sql("""
                CREATE FUNCTION validar_exclusividad_despacho() RETURNS trigger LANGUAGE plpgsql AS $body$
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
                CREATE TRIGGER validar_oferta_exclusiva BEFORE INSERT OR UPDATE OF "Estado", "ConductorId", "SolicitudId"
                    ON intentos_despacho FOR EACH ROW EXECUTE FUNCTION validar_exclusividad_despacho();
                CREATE TRIGGER validar_viaje_exclusivo BEFORE INSERT OR UPDATE OF "Estado", "ConductorId", "SolicitudId"
                    ON viajes FOR EACH ROW EXECUTE FUNCTION validar_exclusividad_despacho();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER validar_oferta_exclusiva ON intentos_despacho; DROP TRIGGER validar_viaje_exclusivo ON viajes; DROP FUNCTION validar_exclusividad_despacho();");
            migrationBuilder.DropForeignKey(
                name: "FK_intentos_despacho_conductores_EmpresaId_ConductorId",
                table: "intentos_despacho");

            migrationBuilder.DropForeignKey(
                name: "FK_intentos_despacho_solicitudes_EmpresaId_SolicitudId",
                table: "intentos_despacho");

            migrationBuilder.DropForeignKey(
                name: "FK_solicitud_eventos_solicitudes_EmpresaId_SolicitudId",
                table: "solicitud_eventos");

            migrationBuilder.DropForeignKey(
                name: "FK_turnos_conductores_EmpresaId_ConductorId",
                table: "turnos");

            migrationBuilder.DropForeignKey(
                name: "FK_turnos_vehiculos_EmpresaId_VehiculoId",
                table: "turnos");

            migrationBuilder.DropForeignKey(
                name: "FK_viajes_conductores_EmpresaId_ConductorId",
                table: "viajes");

            migrationBuilder.DropForeignKey(
                name: "FK_viajes_solicitudes_EmpresaId_SolicitudId",
                table: "viajes");

            migrationBuilder.DropIndex(
                name: "IX_viajes_EmpresaId_ConductorId",
                table: "viajes");

            migrationBuilder.DropIndex(
                name: "IX_viajes_EmpresaId_SolicitudId",
                table: "viajes");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_vehiculos_EmpresaId_Id",
                table: "vehiculos");

            migrationBuilder.DropIndex(
                name: "IX_turnos_EmpresaId_ConductorId",
                table: "turnos");

            migrationBuilder.DropIndex(
                name: "IX_turnos_EmpresaId_VehiculoId",
                table: "turnos");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_solicitudes_EmpresaId_Id",
                table: "solicitudes");

            migrationBuilder.DropIndex(
                name: "IX_solicitud_eventos_EmpresaId_SolicitudId",
                table: "solicitud_eventos");

            migrationBuilder.DropIndex(
                name: "IX_intentos_despacho_EmpresaId_ConductorId",
                table: "intentos_despacho");

            migrationBuilder.DropIndex(
                name: "IX_intentos_despacho_EmpresaId_SolicitudId",
                table: "intentos_despacho");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_conductores_EmpresaId_Id",
                table: "conductores");

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "solicitud_eventos");

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "intentos_despacho");

            migrationBuilder.AddForeignKey(
                name: "FK_intentos_despacho_conductores_ConductorId",
                table: "intentos_despacho",
                column: "ConductorId",
                principalTable: "conductores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_intentos_despacho_solicitudes_SolicitudId",
                table: "intentos_despacho",
                column: "SolicitudId",
                principalTable: "solicitudes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_solicitud_eventos_solicitudes_SolicitudId",
                table: "solicitud_eventos",
                column: "SolicitudId",
                principalTable: "solicitudes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_turnos_conductores_ConductorId",
                table: "turnos",
                column: "ConductorId",
                principalTable: "conductores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_turnos_vehiculos_VehiculoId",
                table: "turnos",
                column: "VehiculoId",
                principalTable: "vehiculos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_viajes_conductores_ConductorId",
                table: "viajes",
                column: "ConductorId",
                principalTable: "conductores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_viajes_solicitudes_SolicitudId",
                table: "viajes",
                column: "SolicitudId",
                principalTable: "solicitudes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
