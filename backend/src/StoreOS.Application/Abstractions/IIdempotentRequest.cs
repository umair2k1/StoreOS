namespace StoreOS.Application.Abstractions;

/// <summary>
/// Implemented by commands that must be idempotent. Controllers copy the Idempotency-Key header into IdempotencyKey.
/// </summary>
public interface IIdempotentRequest
{
    string? IdempotencyKey { get; }

    /// <summary>
    /// Stable, low-cardinality scope. Examples: "pos.checkout", "pos.estimate.convert:{estimateId}", "sales.payment:{saleId}".
    /// </summary>
    string IdempotencyScope { get; }

    /// <summary>
    /// true => a missing key is rejected with idempotency_key_required (400).
    /// </summary>
    bool IdempotencyRequired { get; }
}

public enum IdempotencyOutcome
{
    Started,
    Replay,
    Mismatch,
    InProgress
}

public sealed record IdempotencyBegin(IdempotencyOutcome Outcome, string? ResponseJson);

public interface IIdempotencyStore
{
    Task<IdempotencyBegin> BeginAsync(string key, string scope, string requestHash, CancellationToken ct);
    Task CompleteAsync(string key, string scope, string responseJson, CancellationToken ct);
}
