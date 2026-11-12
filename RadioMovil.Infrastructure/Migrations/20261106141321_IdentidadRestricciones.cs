using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioMovil.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IdentidadRestricciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_sesiones_acceso_EmpresaId_UsuarioId",
                table: "sesiones_acceso",
                columns: new[] { "EmpresaId", "UsuarioId" });

            migrationBuilder.CreateIndex(
                name: "IX_membresias_empresa_EmpresaId_ConductorId",
                table: "membresias_empresa",
                columns: new[] { "EmpresaId", "ConductorId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_membresia_rol_conductor",
                table: "membresias_empresa",
                sql: "(\"Rol\" = 'Conductor' AND \"ConductorId\" IS NOT NULL) OR (\"Rol\" IN ('Administrador','Operador') AND \"ConductorId\" IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_membresias_empresa_conductores_EmpresaId_ConductorId",
                table: "membresias_empresa",
                columns: new[] { "EmpresaId", "ConductorId" },
                principalTable: "conductores",
                principalColumns: new[] { "EmpresaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sesiones_acceso_membresias_empresa_EmpresaId_UsuarioId",
                table: "sesiones_acceso",
                columns: new[] { "EmpresaId", "UsuarioId" },
                principalTable: "membresias_empresa",
                principalColumns: new[] { "EmpresaId", "UsuarioId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_membresias_empresa_conductores_EmpresaId_ConductorId",
                table: "membresias_empresa");

            migrationBuilder.DropForeignKey(
                name: "FK_sesiones_acceso_membresias_empresa_EmpresaId_UsuarioId",
                table: "sesiones_acceso");

            migrationBuilder.DropIndex(
                name: "IX_sesiones_acceso_EmpresaId_UsuarioId",
                table: "sesiones_acceso");

            migrationBuilder.DropIndex(
                name: "IX_membresias_empresa_EmpresaId_ConductorId",
                table: "membresias_empresa");

            migrationBuilder.DropCheckConstraint(
                name: "CK_membresia_rol_conductor",
                table: "membresias_empresa");
        }
    }
}
