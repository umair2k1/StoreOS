namespace StoreOS.Domain.Platform;

public class IdempotencyRecord
{
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public string Key { get; private set; } = default!;
    public string Endpoint { get; private set; } = default!;
    public string RequestHash { get; private set; } = default!;
    public int? ResponseStatus { get; private set; }
    public string? ResponseBodyJson { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private IdempotencyRecord() { }
}
