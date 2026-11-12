using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioMovil.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DespachoManualIdempotente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operaciones_despacho",
                columns: table => new
                {
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ambito = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Clave = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Huella = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SolicitudId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntentoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ViajeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Estado = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    Fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operaciones_despacho", x => new { x.EmpresaId, x.Ambito, x.Clave });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operaciones_despacho");
        }
    }
}
