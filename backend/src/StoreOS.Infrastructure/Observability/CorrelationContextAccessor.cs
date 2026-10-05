using StoreOS.Application.Abstractions;

namespace StoreOS.Infrastructure.Observability;

public class CorrelationContextAccessor : ICorrelationContextAccessor
{
    public string CorrelationId { get; set; } = string.Empty;
}
