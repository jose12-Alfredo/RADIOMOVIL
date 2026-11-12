using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RadioMovil.Infrastructure;
using RADIOMOVIL;

namespace RadioMovil.UnitTests;

internal static class TestIdentity
{
    internal sealed record Identity(Guid UserId, string Email, string Password, Guid Company);

    internal static async Task<Identity> SeedAsync(WebApplicationFactory<Program> factory, Guid company,
        string role = RolesEmpresa.Operador, Guid? driverId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioMovilDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"test-{Guid.NewGuid():N}@example.invalid";
        var password = "Strong!" + Guid.NewGuid().ToString("N") + "8";
        var user = new AppUser { Id = Guid.NewGuid(), UserName = email, Email = email, Activo = true };
        await using var tx = await db.Database.BeginTransactionAsync();
        var result = await manager.CreateAsync(user, password);
        if (!result.Succeeded) throw new Exception(string.Join(", ", result.Errors.Select(x => x.Code)));
        db.Membresias.Add(new EmpresaMembresia { EmpresaId = company, UsuarioId = user.Id, Rol = role, ConductorId = driverId });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new(user.Id, email, password, company);
    }

    internal static async Task<JsonDocument> LoginAndroidAsync(HttpClient client, Identity identity)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = identity.Email, password = identity.Password, empresaId = identity.Company, cliente = "Android" });
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Login Android falló: {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new("Bearer", json.RootElement.GetProperty("accessToken").GetString());
        return json;
    }
}
