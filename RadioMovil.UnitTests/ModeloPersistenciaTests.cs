using Microsoft.EntityFrameworkCore;
using RadioMovil.Domain;
using RadioMovil.Infrastructure;
using Xunit;

namespace RadioMovil.UnitTests;

public sealed class ModeloPersistenciaTests
{
    [Fact]
    public void Modelo_ContieneIndicesParcialesYPostgis()
    {
        var options = new DbContextOptionsBuilder<RadioMovilDbContext>()
            .UseNpgsql("Host=localhost;Database=solo_modelo;Username=placeholder;Password=placeholder", x => x.UseNetTopologySuite()).Options;
        using var db = new RadioMovilDbContext(options);
        var intento = db.Model.FindEntityType(typeof(IntentoDespacho))!;
        Assert.Contains(intento.GetIndexes(), i => i.IsUnique && i.GetFilter()!.Contains("Ofertado"));
        Assert.NotNull(db.Model.FindEntityType(typeof(UbicacionConductor)));
        Assert.NotNull(db.Model.FindEntityType(typeof(SolicitudEvento)));
    }
}
