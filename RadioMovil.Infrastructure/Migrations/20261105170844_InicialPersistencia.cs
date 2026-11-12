using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace RadioMovil.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InicialPersistencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "claves_idempotencia",
                columns: table => new
                {
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ambito = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Clave = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SolicitudId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_claves_idempotencia", x => new { x.EmpresaId, x.Ambito, x.Clave });
                });

            migrationBuilder.CreateTable(
                name: "conductores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nombre = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conductores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "contadores_central",
                columns: table => new
                {
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    CentralId = table.Column<Guid>(type: "uuid", nullable: false),
                    UltimoOrden = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contadores_central", x => new { x.EmpresaId, x.CentralId });
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Contenido = table.Column<string>(type: "jsonb", nullable: false),
                    CreadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublicadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "solicitudes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Origen = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Estado = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ViajeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CentralId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrdenRecepcion = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_solicitudes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "vehiculos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Patente = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehiculos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ubicaciones_conductor",
                columns: table => new
                {
                    ConductorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Posicion = table.Column<Point>(type: "geometry(Point,4326)", nullable: false),
                    RegistradoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ubicaciones_conductor", x => x.ConductorId);
                    table.ForeignKey(
                        name: "FK_ubicaciones_conductor_conductores_ConductorId",
                        column: x => x.ConductorId,
                        principalTable: "conductores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "intentos_despacho",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SolicitudId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConductorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VenceEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Estado = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_intentos_despacho", x => x.Id);
                    table.CheckConstraint("CK_intento_vencimiento", "\"VenceEn\" > \"CreadoEn\"");
                    table.ForeignKey(
                        name: "FK_intentos_despacho_conductores_ConductorId",
                        column: x => x.ConductorId,
                        principalTable: "conductores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_intentos_despacho_solicitudes_SolicitudId",
                        column: x => x.SolicitudId,
                        principalTable: "solicitudes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "solicitud_eventos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SolicitudId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Motivo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_solicitud_eventos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_solicitud_eventos_solicitudes_SolicitudId",
                        column: x => x.SolicitudId,
                        principalTable: "solicitudes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "viajes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    SolicitudId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConductorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AsignadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Estado = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_viajes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_viajes_conductores_ConductorId",
                        column: x => x.ConductorId,
                        principalTable: "conductores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_viajes_solicitudes_SolicitudId",
                        column: x => x.SolicitudId,
                        principalTable: "solicitudes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "turnos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConductorId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehiculoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Fin = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_turnos", x => x.Id);
                    table.CheckConstraint("CK_turno_fechas", "\"Fin\" IS NULL OR \"Fin\" >= \"Inicio\"");
                    table.ForeignKey(
                        name: "FK_turnos_conductores_ConductorId",
                        column: x => x.ConductorId,
                        principalTable: "conductores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_turnos_vehiculos_VehiculoId",
                        column: x => x.VehiculoId,
                        principalTable: "vehiculos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_conductores_EmpresaId_Id",
                table: "conductores",
                columns: new[] { "EmpresaId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_intentos_despacho_ConductorId",
                table: "intentos_despacho",
                column: "ConductorId",
                unique: true,
                filter: "\"Estado\" = 'Ofertado'");

            migrationBuilder.CreateIndex(
                name: "IX_intentos_despacho_SolicitudId",
                table: "intentos_despacho",
                column: "SolicitudId",
                unique: true,
                filter: "\"Estado\" = 'Ofertado'");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_PublicadoEn_CreadoEn",
                table: "outbox",
                columns: new[] { "PublicadoEn", "CreadoEn" });

            migrationBuilder.CreateIndex(
                name: "IX_solicitud_eventos_SolicitudId_Fecha",
                table: "solicitud_eventos",
                columns: new[] { "SolicitudId", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_solicitudes_EmpresaId_CentralId_OrdenRecepcion",
                table: "solicitudes",
                columns: new[] { "EmpresaId", "CentralId", "OrdenRecepcion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_solicitudes_EmpresaId_Id",
                table: "solicitudes",
                columns: new[] { "EmpresaId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_turnos_ConductorId",
                table: "turnos",
                column: "ConductorId",
                unique: true,
                filter: "\"Fin\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_turnos_VehiculoId",
                table: "turnos",
                column: "VehiculoId",
                unique: true,
                filter: "\"Fin\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ubicaciones_conductor_Posicion",
                table: "ubicaciones_conductor",
                column: "Posicion")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_vehiculos_EmpresaId_Id",
                table: "vehiculos",
                columns: new[] { "EmpresaId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vehiculos_EmpresaId_Patente",
                table: "vehiculos",
                columns: new[] { "EmpresaId", "Patente" },
                unique: true);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "claves_idempotencia");

            migrationBuilder.DropTable(
                name: "contadores_central");

            migrationBuilder.DropTable(
                name: "intentos_despacho");

            migrationBuilder.DropTable(
                name: "outbox");

            migrationBuilder.DropTable(
                name: "solicitud_eventos");

            migrationBuilder.DropTable(
                name: "turnos");

            migrationBuilder.DropTable(
                name: "ubicaciones_conductor");

            migrationBuilder.DropTable(
                name: "viajes");

            migrationBuilder.DropTable(
                name: "vehiculos");

            migrationBuilder.DropTable(
                name: "conductores");

            migrationBuilder.DropTable(
                name: "solicitudes");
        }
    }
}
