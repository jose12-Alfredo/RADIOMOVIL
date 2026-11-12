using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioMovil.Infrastructure;
using RADIOMOVIL;
using Xunit;

namespace RadioMovil.UnitTests;

public sealed class EnterpriseAuthNeonTests
{
    private static WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(x => x.UseEnvironment("Testing"));
    private static HttpClient Client(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    [NeonFact]
    public async Task Me_WebIdentidadAisladaSinCache_CsrfYRevocacion()
    {
        using var factory = Factory(); using var browser = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/auth/me")).StatusCode);
        var company = Guid.NewGuid(); var foreign = Guid.NewGuid();
        var user = await TestIdentity.SeedAsync(factory, company);
        var other = await TestIdentity.SeedAsync(factory, foreign, RolesEmpresa.Administrador);
        using (var csrfResponse = await browser.GetAsync("/api/v1/auth/csrf"))
        {
            var cookies = string.Join(";", csrfResponse.Headers.GetValues("Set-Cookie"));
            Assert.Contains("rm.csrf=", cookies);
            Assert.Contains("secure", cookies, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("httponly", cookies, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=strict", cookies, StringComparison.OrdinalIgnoreCase);
            using var csrf = JsonDocument.Parse(await csrfResponse.Content.ReadAsStringAsync());
            browser.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.RootElement.GetProperty("token").GetString());
        }
        using (var login = await browser.PostAsJsonAsync("/api/v1/auth/login", new
            { email = user.Email, password = user.Password, empresaId = company, cliente = "Web" }))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var cookies = string.Join(";", login.Headers.GetValues("Set-Cookie"));
            Assert.Contains("rm.web=", cookies);
            Assert.Contains("secure", cookies, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("httponly", cookies, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=strict", cookies, StringComparison.OrdinalIgnoreCase);
        }
        // Parámetros ajenos no seleccionan identidad ni empresa: el endpoint no los acepta como ámbito.
        using (var response = await browser.GetAsync($"/api/v1/auth/me?empresaId={foreign}&usuarioId={other.UserId}"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var me = json.RootElement;
            Assert.Equal(user.UserId, me.GetProperty("usuarioId").GetGuid());
            Assert.Equal(company, me.GetProperty("empresaId").GetGuid());
            Assert.Equal(user.Email, me.GetProperty("email").GetString());
            Assert.Equal(RolesEmpresa.Operador, Assert.Single(me.GetProperty("roles").EnumerateArray()).GetString());
            Assert.True(me.GetProperty("expiraEn").GetDateTimeOffset() > DateTimeOffset.UtcNow);
            Assert.Equal(5, me.EnumerateObject().Count());
        }
        using var otherClient = Client(factory);
        using var otherToken = await TestIdentity.LoginAndroidAsync(otherClient, other);
        using var otherMe = JsonDocument.Parse(await otherClient.GetStringAsync("/api/v1/auth/me"));
        Assert.Equal(foreign, otherMe.RootElement.GetProperty("empresaId").GetGuid());
        browser.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsJsonAsync("/api/v1/auth/logout", new { })).StatusCode);
        using var authenticatedCsrf = JsonDocument.Parse(await browser.GetStringAsync("/api/v1/auth/csrf"));
        browser.DefaultRequestHeaders.Add("X-CSRF-TOKEN", authenticatedCsrf.RootElement.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsJsonAsync("/api/v1/auth/logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [NeonFact]
    public async Task Me_ExpiradaRevocadaOUsuarioInactivo401_MembresiaInactiva403()
    {
        using var factory = Factory();
        foreach (var scenario in new[] { "expired", "revoked", "inactive-user", "inactive-membership" })
        {
            using var client = Client(factory);
            var user = await TestIdentity.SeedAsync(factory, Guid.NewGuid());
            using var token = await TestIdentity.LoginAndroidAsync(client, user);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioMovilDbContext>();
            if (scenario == "expired")
                await db.SesionesAcceso.Where(s => s.UsuarioId == user.UserId)
                    .ExecuteUpdateAsync(x => x.SetProperty(s => s.ExpiraEn, DateTimeOffset.UtcNow.AddSeconds(-1)));
            if (scenario == "revoked")
                await db.SesionesAcceso.Where(s => s.UsuarioId == user.UserId)
                    .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevocadaEn, DateTimeOffset.UtcNow));
            if (scenario == "inactive-user")
                await db.Users.Where(u => u.Id == user.UserId).ExecuteUpdateAsync(x => x.SetProperty(u => u.Activo, false));
            if (scenario == "inactive-membership")
                await db.Membresias.Where(m => m.UsuarioId == user.UserId && m.EmpresaId == user.Company)
                    .ExecuteUpdateAsync(x => x.SetProperty(m => m.Activa, false));
            Assert.Equal(scenario == "inactive-membership" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized,
                (await client.GetAsync("/api/v1/auth/me")).StatusCode);
        }
    }

    [NeonFact]
    public async Task BootstrapLocal_SoloUnaEmpresaSinRegistroPublico()
    {
        var company = Guid.NewGuid();
        var email = $"bootstrap-{Guid.NewGuid():N}@example.invalid";
        var password = "Secure!" + Guid.NewGuid().ToString("N") + "9";
        Environment.SetEnvironmentVariable("RADIOMOVIL_BOOTSTRAP_ADMIN", "1");
        Environment.SetEnvironmentVariable("RADIOMOVIL_BOOTSTRAP_EMPRESA_ID", company.ToString());
        Environment.SetEnvironmentVariable("RADIOMOVIL_BOOTSTRAP_EMAIL", email);
        Environment.SetEnvironmentVariable("RADIOMOVIL_BOOTSTRAP_PASSWORD", password);
        try
        {
            using var factory = Factory(); using var client = Client(factory);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/usuarios", new
                { email = "intruder@example.invalid", password = "SecurePassword!12345", rol = RolesEmpresa.Administrador })).StatusCode);
            using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password, empresaId = company, cliente = "Android" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioMovilDbContext>();
            Assert.Equal(1, await db.Membresias.CountAsync(m => m.EmpresaId == company && m.Rol == RolesEmpresa.Administrador));
            Assert.True(await db.AuditoriaAcceso.AnyAsync(a => a.EmpresaId == company && a.Tipo == "BootstrapAdmin"));
        }
        finally
        {
            foreach (var name in new[] { "RADIOMOVIL_BOOTSTRAP_ADMIN", "RADIOMOVIL_BOOTSTRAP_EMPRESA_ID", "RADIOMOVIL_BOOTSTRAP_EMAIL", "RADIOMOVIL_BOOTSTRAP_PASSWORD" })
                Environment.SetEnvironmentVariable(name, null);
        }
    }

    [NeonFact]
    public async Task LoginAndroid_BearerValidaPermisosYLogoutRevoca()
    {
        using var factory = Factory(); using var client = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/solicitudes")).StatusCode);
        var company = Guid.NewGuid();
        var identity = await TestIdentity.SeedAsync(factory, company);
        using (var invalid = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = identity.Email, password = "Incorrect!12345", empresaId = company, cliente = "Android" }))
            Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        using var tokens = await TestIdentity.LoginAndroidAsync(client, identity);
        using (var invalidRefresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = "not-a-token" }))
            Assert.Equal(HttpStatusCode.Unauthorized, invalidRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/solicitudes")).StatusCode);
        var old = tokens.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(old));
        using (var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new { }))
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/solicitudes")).StatusCode);
        using var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RootElement.GetProperty("refreshToken").GetString() });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        using var keyOnly = Client(factory);
        keyOnly.DefaultRequestHeaders.Add("X-Test-Api-Key", "legacy-no-access");
        Assert.Equal(HttpStatusCode.Unauthorized, (await keyOnly.GetAsync("/api/v1/solicitudes")).StatusCode);
    }

    [NeonFact]
    public async Task Web_CsrfCookieLogoutYExpiracionDeSesion()
    {
        using var factory = Factory(); using var browser = Client(factory);
        var company = Guid.NewGuid();
        var user = await TestIdentity.SeedAsync(factory, company);
        using var csrf = JsonDocument.Parse(await browser.GetStringAsync("/api/v1/auth/csrf"));
        var token = csrf.RootElement.GetProperty("token").GetString();
        var login = new { email = user.Email, password = user.Password, empresaId = company, cliente = "Web" };
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsJsonAsync("/api/v1/auth/login", login)).StatusCode);
        browser.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
        using (var response = await browser.PostAsJsonAsync("/api/v1/auth/login", login))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("rm.web=", string.Join(";", response.Headers.GetValues("Set-Cookie")));
            Assert.Contains("secure", string.Join(";", response.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("httponly", string.Join(";", response.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/v1/solicitudes")).StatusCode);
        browser.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using (var rejected = await browser.PostAsJsonAsync("/api/v1/solicitudes", new { centralId = Guid.NewGuid(), origen = "CSRF" }))
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var authenticatedCsrf = JsonDocument.Parse(await browser.GetStringAsync("/api/v1/auth/csrf"));
        browser.DefaultRequestHeaders.Add("X-CSRF-TOKEN", authenticatedCsrf.RootElement.GetProperty("token").GetString());
        using (var logout = await browser.PostAsJsonAsync("/api/v1/auth/logout", new { }))
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/solicitudes")).StatusCode);
        browser.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var newCsrf = JsonDocument.Parse(await browser.GetStringAsync("/api/v1/auth/csrf"));
        browser.DefaultRequestHeaders.Add("X-CSRF-TOKEN", newCsrf.RootElement.GetProperty("token").GetString());
        using var relogin = await browser.PostAsJsonAsync("/api/v1/auth/login", login);
        Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioMovilDbContext>();
        await db.SesionesAcceso.Where(s => s.UsuarioId == user.UserId && s.RevocadaEn == null)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.ExpiraEn, DateTimeOffset.UtcNow.AddSeconds(-1)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/solicitudes")).StatusCode);
    }

    [NeonFact]
    public async Task Android_RefreshUnSoloUsoRevocacionYPermisosPorEmpresa()
    {
        using var factory = Factory(); using var client = Client(factory);
        var company = Guid.NewGuid(); var foreign = Guid.NewGuid();
        var admin = await TestIdentity.SeedAsync(factory, company, RolesEmpresa.Administrador);
        var user = await TestIdentity.SeedAsync(factory, company);
        using (var wrong = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = user.Email, password = user.Password, empresaId = foreign, cliente = "Android" }))
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        using var old = await TestIdentity.LoginAndroidAsync(client, user);
        var refreshToken = old.RootElement.GetProperty("refreshToken").GetString();
        client.DefaultRequestHeaders.Authorization = null;
        using var renewed = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken });
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        using var newTokens = JsonDocument.Parse(await renewed.Content.ReadAsStringAsync());
        using var replay = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", old.RootElement.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/solicitudes")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", newTokens.RootElement.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/solicitudes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/auth/usuarios", new { email = "nope@example.invalid", password = "Password!123456", rol = RolesEmpresa.Operador })).StatusCode);
        using var adminClient = Client(factory);
        using var adminToken = await TestIdentity.LoginAndroidAsync(adminClient, admin);
        var newEmail = $"registered-{Guid.NewGuid():N}@example.invalid";
        using (var created = await adminClient.PostAsJsonAsync("/api/v1/auth/usuarios", new
            { email = newEmail, password = "SecurePassword!12345", rol = RolesEmpresa.Operador }))
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using (var registeredLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new
            { email = newEmail, password = "SecurePassword!12345", empresaId = company, cliente = "Android" }))
            Assert.Equal(HttpStatusCode.OK, registeredLogin.StatusCode);
        var foreignUser = await TestIdentity.SeedAsync(factory, foreign);
        using (var foreignAdmin = await adminClient.PutAsJsonAsync($"/api/v1/auth/usuarios/{foreignUser.UserId}",
            new { rol = RolesEmpresa.Operador, conductorId = (Guid?)null, activo = false }))
            Assert.Equal(HttpStatusCode.NotFound, foreignAdmin.StatusCode);
        using var disable = await adminClient.PutAsJsonAsync($"/api/v1/auth/usuarios/{user.UserId}", new { rol = RolesEmpresa.Operador, conductorId = (Guid?)null, activo = false });
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/solicitudes")).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = newTokens.RootElement.GetProperty("refreshToken").GetString() })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password, empresaId = company, cliente = "Android" })).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioMovilDbContext>();
        Assert.True(await db.AuditoriaAcceso.AnyAsync(a => a.EmpresaId == company && a.UsuarioId == admin.UserId && a.Tipo == "PermisosModificados" && a.ObjetoId == user.UserId));
    }

    [NeonFact]
    public async Task Conductor_OfertaPropiaYReplayAutorizado_AjenaDenegada()
    {
        using var factory = Factory(); using var adminClient = Client(factory);
        var company = Guid.NewGuid(); var admin = await TestIdentity.SeedAsync(factory, company, RolesEmpresa.Administrador);
        using var adminTokens = await TestIdentity.LoginAndroidAsync(adminClient, admin);
        Guid firstDriver; Guid otherDriver; Guid requestId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioMovilDbContext>();
            var d1 = new RadioMovil.Domain.Conductor(company, "Chofer A"); var d2 = new RadioMovil.Domain.Conductor(company, "Chofer B");
            var v1 = new RadioMovil.Domain.Vehiculo(company, "SE" + Guid.NewGuid().ToString("N")[..10]);
            var v2 = new RadioMovil.Domain.Vehiculo(company, "SE" + Guid.NewGuid().ToString("N")[..10]);
            db.Conductores.AddRange(d1, d2); db.Vehiculos.AddRange(v1, v2);
            db.Turnos.AddRange(new RadioMovil.Domain.Turno(company, d1.Id, v1.Id, DateTimeOffset.UtcNow.AddMinutes(-2)),
                new RadioMovil.Domain.Turno(company, d2.Id, v2.Id, DateTimeOffset.UtcNow.AddMinutes(-2)));
            await db.SaveChangesAsync();
            var central = await TestCentrales.CreateAsync(db, company);
            (requestId, _) = await new RegistroSolicitudes(db).RegistrarAsync(company, central, admin.UserId, "Terminal", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
            firstDriver = d1.Id; otherDriver = d2.Id;
        }
        var first = await TestIdentity.SeedAsync(factory, company, RolesEmpresa.Conductor, firstDriver);
        var second = await TestIdentity.SeedAsync(factory, company, RolesEmpresa.Conductor, otherDriver);
        using var firstClient = Client(factory); using var firstToken = await TestIdentity.LoginAndroidAsync(firstClient, first);
        using var otherClient = Client(factory); using var otherToken = await TestIdentity.LoginAndroidAsync(otherClient, second);
        Assert.Equal(HttpStatusCode.Forbidden, (await firstClient.GetAsync("/api/v1/despacho/candidatos")).StatusCode);
        var offerPath = $"/api/v1/despacho/solicitudes/{requestId}/ofertas";
        using var offerReq = new HttpRequestMessage(HttpMethod.Post, offerPath) { Content = JsonContent.Create(new { conductorId = firstDriver }) };
        offerReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var offered = await adminClient.SendAsync(offerReq);
        Assert.Equal(HttpStatusCode.Created, offered.StatusCode);
        using var offeredJson = JsonDocument.Parse(await offered.Content.ReadAsStringAsync());
        var offer = offeredJson.RootElement.GetProperty("intentoId").GetGuid();
        async Task<HttpStatusCode> Reject(HttpClient client, Guid id)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{offerPath}/{id}/rechazar")
            { Content = JsonContent.Create(new { motivo = "No disponible" }) };
            req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            using var res = await client.SendAsync(req);
            return res.StatusCode;
        }
        Assert.Equal(HttpStatusCode.Forbidden, await Reject(otherClient, offer));
        var acceptPath = $"{offerPath}/{offer}/aceptar";
        async Task<HttpStatusCode> Accept(HttpClient client, string key)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, acceptPath) { Content = JsonContent.Create(new { }) };
            req.Headers.Add("Idempotency-Key", key);
            using var res = await client.SendAsync(req);
            return res.StatusCode;
        }
        var key = Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.Forbidden, await Accept(otherClient, key));
        Assert.Equal(HttpStatusCode.Forbidden, await Accept(adminClient, key));
        Assert.Equal(HttpStatusCode.OK, await Accept(firstClient, key));
        Assert.Equal(HttpStatusCode.Forbidden, await Accept(otherClient, key)); // incluso resultado idempotente
        Assert.Equal(HttpStatusCode.OK, await Accept(firstClient, key));
        await using var check = factory.Services.CreateAsyncScope();
        var dbCheck = check.ServiceProvider.GetRequiredService<RadioMovilDbContext>();
        var trip = await dbCheck.Viajes.SingleAsync(x => x.SolicitudId == requestId);
        async Task<HttpStatusCode> Cancel(HttpClient client, string key)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/despacho/solicitudes/{requestId}/viajes/{trip.Id}/cancelar-conductor")
            { Content = JsonContent.Create(new { motivo = "Antes de recoger" }) };
            req.Headers.Add("Idempotency-Key", key);
            using var res = await client.SendAsync(req);
            return res.StatusCode;
        }
        var cancelKey = Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.Forbidden, await Cancel(otherClient, cancelKey));
        Assert.Equal(HttpStatusCode.OK, await Cancel(firstClient, cancelKey));
        Assert.Equal(HttpStatusCode.Forbidden, await Cancel(otherClient, cancelKey));
        Assert.Equal(1, await dbCheck.Eventos.CountAsync(x => x.SolicitudId == requestId && x.Tipo == "OfertaAceptada"));
        Assert.Equal(1, await dbCheck.Viajes.CountAsync(x => x.SolicitudId == requestId));
        using var retryOfferReq = new HttpRequestMessage(HttpMethod.Post, offerPath) { Content = JsonContent.Create(new { conductorId = firstDriver }) };
        retryOfferReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var retryOffer = await adminClient.SendAsync(retryOfferReq);
        Assert.Equal(HttpStatusCode.Created, retryOffer.StatusCode);
        using var retryOfferJson = JsonDocument.Parse(await retryOffer.Content.ReadAsStringAsync());
        var secondOffer = retryOfferJson.RootElement.GetProperty("intentoId").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, await Reject(otherClient, secondOffer));
        Assert.Equal(HttpStatusCode.OK, await Reject(firstClient, secondOffer));
    }

    [NeonFact]
    public async Task RetirarRolAdministrador_InvalidaTokenAnteriorYRestringeSiguienteSesion()
    {
        using var factory = Factory(); using var client = Client(factory); using var ownerClient = Client(factory);
        var company = Guid.NewGuid();
        var owner = await TestIdentity.SeedAsync(factory, company, RolesEmpresa.Administrador);
        var target = await TestIdentity.SeedAsync(factory, company, RolesEmpresa.Administrador);
        using var ownerToken = await TestIdentity.LoginAndroidAsync(ownerClient, owner);
        using var oldToken = await TestIdentity.LoginAndroidAsync(client, target);
        using (var changed = await ownerClient.PutAsJsonAsync($"/api/v1/auth/usuarios/{target.UserId}",
            new { rol = RolesEmpresa.Operador, conductorId = (Guid?)null, activo = true }))
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/solicitudes")).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        using var newToken = await TestIdentity.LoginAndroidAsync(client, target);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/solicitudes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/auth/usuarios",
            new { email = $"blocked-{Guid.NewGuid():N}@example.invalid", password = "SecurePassword!12345", rol = RolesEmpresa.Operador })).StatusCode);
    }
}
