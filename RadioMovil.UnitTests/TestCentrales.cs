using RadioMovil.Infrastructure;

namespace RadioMovil.UnitTests;

internal static class TestCentrales
{
    internal static async Task<Guid> CreateAsync(RadioMovilDbContext db, Guid empresa, Guid? id = null)
    {
        var central = new Central { Id = id ?? Guid.NewGuid(), EmpresaId = empresa, Nombre = "Central prueba " + Guid.NewGuid().ToString("N") };
        db.Centrales.Add(central);
        await db.SaveChangesAsync();
        return central.Id;
    }
}
