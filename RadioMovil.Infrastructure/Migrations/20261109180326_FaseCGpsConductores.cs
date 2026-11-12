using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RadioMovil.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FaseCGpsConductores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CapturadaEn",
                table: "ubicaciones_conductor",
                type: "timestamp with time zone",
                nullable: false);

            migrationBuilder.AddColumn<Guid>(
                name: "EmpresaId",
                table: "ubicaciones_conductor",
                type: "uuid",
                nullable: false);

            migrationBuilder.AddColumn<double>(
                name: "PrecisionMetros",
                table: "ubicaciones_conductor",
                type: "double precision",
                nullable: false);

            migrationBuilder.AddColumn<Guid>(
                name: "TurnoId",
                table: "ubicaciones_conductor",
                type: "uuid",
                nullable: false);

            migrationBuilder.CreateTable(
                name: "conductor_vehiculos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConductorId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehiculoId = table.Column<Guid>(type: "uuid", nullable: false),
                    AutorizadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AutorizadaPor = table.Column<Guid>(type: "uuid", nullable: false),
                    RevocadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevocadaPor = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conductor_vehiculos", x => x.Id);
                    table.CheckConstraint("CK_autorizacion_revocacion", "(\"RevocadaEn\" IS NULL) = (\"RevocadaPor\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_conductor_vehiculos_conductores_EmpresaId_ConductorId",
                        columns: x => new { x.EmpresaId, x.ConductorId },
                        principalTable: "conductores",
                        principalColumns: new[] { "EmpresaId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_conductor_vehiculos_vehiculos_EmpresaId_VehiculoId",
                        columns: x => new { x.EmpresaId, x.VehiculoId },
                        principalTable: "vehiculos",
                        principalColumns: new[] { "EmpresaId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ubicaciones_historial",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConductorId = table.Column<Guid>(type: "uuid", nullable: false),
                    TurnoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Posicion = table.Column<Point>(type: "geometry(Point,4326)", nullable: false),
                    PrecisionMetros = table.Column<double>(type: "double precision", nullable: false),
                    CapturadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecibidaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ubicaciones_historial", x => x.Id);
                    table.CheckConstraint("CK_historial_precision", "\"PrecisionMetros\" > 0");
                    table.ForeignKey(
                        name: "FK_ubicaciones_historial_conductores_EmpresaId_ConductorId",
                        columns: x => new { x.EmpresaId, x.ConductorId },
                        principalTable: "conductores",
                        principalColumns: new[] { "EmpresaId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ubicaciones_historial_turnos_TurnoId",
                        column: x => x.TurnoId,
                        principalTable: "turnos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ubicaciones_conductor_EmpresaId_ConductorId",
                table: "ubicaciones_conductor",
                columns: new[] { "EmpresaId", "ConductorId" });

            migrationBuilder.CreateIndex(
                name: "IX_ubicaciones_conductor_EmpresaId_RegistradoEn",
                table: "ubicaciones_conductor",
                columns: new[] { "EmpresaId", "RegistradoEn" });

            migrationBuilder.CreateIndex(
                name: "IX_ubicaciones_conductor_TurnoId",
                table: "ubicaciones_conductor",
                column: "TurnoId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ubicacion_precision",
                table: "ubicaciones_conductor",
                sql: "\"PrecisionMetros\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_conductor_vehiculos_ConductorId_VehiculoId",
                table: "conductor_vehiculos",
                columns: new[] { "ConductorId", "VehiculoId" },
                unique: true,
                filter: "\"RevocadaEn\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_conductor_vehiculos_EmpresaId_ConductorId",
                table: "conductor_vehiculos",
                columns: new[] { "EmpresaId", "ConductorId" });

            migrationBuilder.CreateIndex(
                name: "IX_conductor_vehiculos_EmpresaId_VehiculoId",
                table: "conductor_vehiculos",
                columns: new[] { "EmpresaId", "VehiculoId" });

            migrationBuilder.CreateIndex(
                name: "IX_ubicaciones_historial_EmpresaId_ConductorId_RecibidaEn",
                table: "ubicaciones_historial",
                columns: new[] { "EmpresaId", "ConductorId", "RecibidaEn" });

            migrationBuilder.CreateIndex(
                name: "IX_ubicaciones_historial_TurnoId",
                table: "ubicaciones_historial",
                column: "TurnoId");

            migrationBuilder.AddForeignKey(
                name: "FK_ubicaciones_conductor_conductores_EmpresaId_ConductorId",
                table: "ubicaciones_conductor",
                columns: new[] { "EmpresaId", "ConductorId" },
                principalTable: "conductores",
                principalColumns: new[] { "EmpresaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ubicaciones_conductor_turnos_TurnoId",
                table: "ubicaciones_conductor",
                column: "TurnoId",
                principalTable: "turnos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ubicaciones_conductor_conductores_EmpresaId_ConductorId",
                table: "ubicaciones_conductor");

            migrationBuilder.DropForeignKey(
                name: "FK_ubicaciones_conductor_turnos_TurnoId",
                table: "ubicaciones_conductor");

            migrationBuilder.DropTable(
                name: "conductor_vehiculos");

            migrationBuilder.DropTable(
                name: "ubicaciones_historial");

            migrationBuilder.DropIndex(
                name: "IX_ubicaciones_conductor_EmpresaId_ConductorId",
                table: "ubicaciones_conductor");

            migrationBuilder.DropIndex(
                name: "IX_ubicaciones_conductor_EmpresaId_RegistradoEn",
                table: "ubicaciones_conductor");

            migrationBuilder.DropIndex(
                name: "IX_ubicaciones_conductor_TurnoId",
                table: "ubicaciones_conductor");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ubicacion_precision",
                table: "ubicaciones_conductor");

            migrationBuilder.DropColumn(
                name: "CapturadaEn",
                table: "ubicaciones_conductor");

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "ubicaciones_conductor");

            migrationBuilder.DropColumn(
                name: "PrecisionMetros",
                table: "ubicaciones_conductor");

            migrationBuilder.DropColumn(
                name: "TurnoId",
                table: "ubicaciones_conductor");
        }
    }
}
