namespace RadioMovil.Application;

// Contrato para 6.2. Ningún adaptador se registra sin un broker real verificado.
public sealed record OutboxDelivery(Guid EventId, Guid EmpresaId, string Type, string Json, DateTimeOffset CreatedAt);
public interface IOutboxTransport
{
    // Solo true después de confirmación positiva del broker Y enrutamiento comprobado.
    Task<bool> PublishConfirmedAndRoutedAsync(OutboxDelivery delivery, CancellationToken ct);
}
