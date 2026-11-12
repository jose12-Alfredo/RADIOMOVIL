using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RadioMovil.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IdentidadEmpresarial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auditoria_accesos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    Fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ObjetoId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auditoria_accesos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "claves_proteccion_datos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_claves_proteccion_datos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "roles_identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles_identity", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "usuarios_identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usuarios_identity", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "claims_rol_identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_claims_rol_identity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_claims_rol_identity_roles_identity_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles_identity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "claims_usuario_identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_claims_usuario_identity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_claims_usuario_identity_usuarios_identity_UserId",
                        column: x => x.UserId,
                        principalTable: "usuarios_identity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "logins_identity",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_logins_identity", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_logins_identity_usuarios_identity_UserId",
                        column: x => x.UserId,
                        principalTable: "usuarios_identity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "membresias_empresa",
                columns: table => new
                {
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConductorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Activa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membresias_empresa", x => new { x.EmpresaId, x.UsuarioId });
                    table.ForeignKey(
                        name: "FK_membresias_empresa_usuarios_identity_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "usuarios_identity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "roles_usuario_identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles_usuario_identity", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_roles_usuario_identity_roles_identity_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles_identity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_roles_usuario_identity_usuarios_identity_UserId",
                        column: x => x.UserId,
                        principalTable: "usuarios_identity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sesiones_acceso",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiraEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevocadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VersionRenovacion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sesiones_acceso", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sesiones_acceso_usuarios_identity_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "usuarios_identity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tokens_usuario_identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tokens_usuario_identity", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_tokens_usuario_identity_usuarios_identity_UserId",
                        column: x => x.UserId,
                        principalTable: "usuarios_identity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auditoria_accesos_EmpresaId_Fecha",
                table: "auditoria_accesos",
                columns: new[] { "EmpresaId", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_claims_rol_identity_RoleId",
                table: "claims_rol_identity",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_claims_usuario_identity_UserId",
                table: "claims_usuario_identity",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_logins_identity_UserId",
                table: "logins_identity",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_membresias_empresa_ConductorId",
                table: "membresias_empresa",
                column: "ConductorId",
                unique: true,
                filter: "\"ConductorId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_membresias_empresa_UsuarioId",
                table: "membresias_empresa",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "roles_identity",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_roles_usuario_identity_RoleId",
                table: "roles_usuario_identity",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_sesiones_acceso_UsuarioId_RevocadaEn",
                table: "sesiones_acceso",
                columns: new[] { "UsuarioId", "RevocadaEn" });

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "usuarios_identity",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "usuarios_identity",
                column: "NormalizedUserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auditoria_accesos");

            migrationBuilder.DropTable(
                name: "claims_rol_identity");

            migrationBuilder.DropTable(
                name: "claims_usuario_identity");

            migrationBuilder.DropTable(
                name: "claves_proteccion_datos");

            migrationBuilder.DropTable(
                name: "logins_identity");

            migrationBuilder.DropTable(
                name: "membresias_empresa");

            migrationBuilder.DropTable(
                name: "roles_usuario_identity");

            migrationBuilder.DropTable(
                name: "sesiones_acceso");

            migrationBuilder.DropTable(
                name: "tokens_usuario_identity");

            migrationBuilder.DropTable(
                name: "roles_identity");

            migrationBuilder.DropTable(
                name: "usuarios_identity");
        }
    }
}
