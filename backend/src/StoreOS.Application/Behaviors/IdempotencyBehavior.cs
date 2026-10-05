using System.Security.Cryptography;
using System.Text.Json;
using MediatR;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Application.Behaviors;

public sealed class IdempotencyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IIdempotencyStore _store;
    private readonly IdempotencyReplayFlag _flag;

    public IdempotencyBehavior(IIdempotencyStore store, IdempotencyReplayFlag flag)
    {
        _store = store;
        _flag = flag;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not IIdempotentRequest idem) return await next();

        if (string.IsNullOrWhiteSpace(idem.IdempotencyKey))
        {
            if (idem.IdempotencyRequired)
                throw new DomainException("idempotency_key_required", "The Idempotency-Key header is required for this operation.");
            return await next();
        }

        if (!Guid.TryParse(idem.IdempotencyKey, out _))
            throw new DomainException("idempotency_key_invalid", "Idempotency-Key must be a UUID.");

        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, Json)));
        var begin = await _store.BeginAsync(idem.IdempotencyKey, idem.IdempotencyScope, hash, ct);

        switch (begin.Outcome)
        {
            case IdempotencyOutcome.Mismatch:
                throw new DomainException("idempotency_key_reuse", "This Idempotency-Key was already used with a different request.");
            case IdempotencyOutcome.InProgress:
                throw new DomainException("idempotency_in_progress", "A request with this Idempotency-Key is still being processed.");
            case IdempotencyOutcome.Replay:
                _flag.Replayed = true;
                return JsonSerializer.Deserialize<TResponse>(begin.ResponseJson!, Json)!;
        }

        var response = await next();
        await _store.CompleteAsync(idem.IdempotencyKey, idem.IdempotencyScope, JsonSerializer.Serialize(response, Json), ct);
        return response;
    }
}

/// <summary>
/// Scoped flag read by controllers to add the 'Idempotent-Replayed: true' response header.
/// </summary>
public sealed class IdempotencyReplayFlag
{
    public bool Replayed { get; set; }
}
