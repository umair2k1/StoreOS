using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Polly;
using Polly.Retry;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Infrastructure.Persistence;

public sealed class SerializationRetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly ResiliencePipeline Pipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<Exception>(IsSerializationFailure),
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromMilliseconds(25),
            MaxDelay = TimeSpan.FromMilliseconds(250)
        })
        .Build();

    private readonly IServiceProvider _services;
    public SerializationRetryBehavior(IServiceProvider services) => _services = services;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        // Only OUTERMOST read-write requests are retried. Queries are never retried; a nested Send() inside an open
        // transaction must never retry on its own (the surrounding transaction is already aborted).
        if (request is IReadOnlyRequest or IReportingRequest) return await next();
        if (_services.GetRequiredService<StoreOsDbContext>().Database.CurrentTransaction is not null) return await next();

        try
        {
            return await Pipeline.ExecuteAsync(async _ => await next(), ct);
        }
        catch (Exception ex) when (IsSerializationFailure(ex))
        {
            throw new DomainException("serialization_conflict",
                "The system is busy processing a conflicting change. Please try again.");
        }
    }

    public static bool IsSerializationFailure(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is PostgresException { SqlState: "40001" or "40P01" }) return true;
        return false;
    }
}
