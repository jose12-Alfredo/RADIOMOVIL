using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioMovil.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WorkersRecuperacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "incidencias_worker",
                columns: table => new
                {
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ReferenciaId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrimeraDeteccion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UltimaDeteccion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProximoIntento = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AtendidaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Intentos = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incidencias_worker", x => new { x.EmpresaId, x.Tipo, x.ReferenciaId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_intentos_despacho_VenceEn_Id",
                table: "intentos_despacho",
                columns: new[] { "VenceEn", "Id" },
                filter: "\"Estado\" = 'Ofertado'");

            migrationBuilder.CreateIndex(
                name: "IX_incidencias_worker_AtendidaEn_ProximoIntento",
                table: "incidencias_worker",
                columns: new[] { "AtendidaEn", "ProximoIntento" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incidencias_worker");

            migrationBuilder.DropIndex(
                name: "IX_intentos_despacho_VenceEn_Id",
                table: "intentos_despacho");
        }
    }
}
