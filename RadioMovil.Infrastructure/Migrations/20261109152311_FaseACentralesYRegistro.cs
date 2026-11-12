using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace RadioMovil.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FaseACentralesYRegistro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Destino",
                table: "solicitudes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NombrePasajero",
                table: "solicitudes",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Observaciones",
                table: "solicitudes",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Point>(
                name: "PuntoRecogida",
                table: "solicitudes",
                type: "geometry(Point,4326)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TelefonoPasajero",
                table: "solicitudes",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "centrales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nombre = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Activa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_centrales", x => new { x.EmpresaId, x.Id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_centrales_EmpresaId_Nombre",
                table: "centrales",
                columns: new[] { "EmpresaId", "Nombre" },
                unique: true);

            // Preservar las solicitudes y contadores históricos antes de exigir la FK.
            // No convertir estos nombres técnicos en una central operativa elegible.
            migrationBuilder.Sql("""
                INSERT INTO centrales ("EmpresaId", "Id", "Nombre", "Activa")
                SELECT "EmpresaId", "CentralId", 'Histórica ' || "CentralId"::text, false
                FROM (SELECT "EmpresaId", "CentralId" FROM solicitudes
                      UNION SELECT "EmpresaId", "CentralId" FROM contadores_central) anteriores
                ON CONFLICT ("EmpresaId", "Id") DO NOTHING;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_solicitudes_centrales_EmpresaId_CentralId",
                table: "solicitudes",
                columns: new[] { "EmpresaId", "CentralId" },
                principalTable: "centrales",
                principalColumns: new[] { "EmpresaId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_solicitudes_centrales_EmpresaId_CentralId",
                table: "solicitudes");

            migrationBuilder.DropTable(
                name: "centrales");

            migrationBuilder.DropColumn(
                name: "Destino",
                table: "solicitudes");

            migrationBuilder.DropColumn(
                name: "NombrePasajero",
                table: "solicitudes");

            migrationBuilder.DropColumn(
                name: "Observaciones",
                table: "solicitudes");

            migrationBuilder.DropColumn(
                name: "PuntoRecogida",
                table: "solicitudes");

            migrationBuilder.DropColumn(
                name: "TelefonoPasajero",
                table: "solicitudes");
        }
    }
}
