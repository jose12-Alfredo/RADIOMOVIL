using Xunit;

// Las pruebas HTTP usan credenciales de proceso para el ámbito Testing; nunca
// ejecutarlas junto a otra WebApplicationFactory con variables distintas.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
