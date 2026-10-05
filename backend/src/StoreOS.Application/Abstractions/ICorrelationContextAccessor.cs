namespace StoreOS.Application.Abstractions;

public interface ICorrelationContextAccessor
{
    string CorrelationId { get; set; }
}
