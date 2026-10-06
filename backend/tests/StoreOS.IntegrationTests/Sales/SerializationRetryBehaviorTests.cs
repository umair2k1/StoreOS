using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Sales;

public record ProbeRequest(string Data) : IRequest<string>;
public record ProbeReadOnlyRequest(string Data) : IRequest<string>, IReadOnlyRequest;

public class SerializationRetryBehaviorTests
{
    private static IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy;Username=dummy;Password=dummy")
            .Options;
        services.AddScoped(_ => new StoreOsDbContext(options, new NullAccessScope()));
        return services.BuildServiceProvider();
    }

    private static PostgresException CreatePostgresException(string sqlState)
    {
        return new PostgresException(
            "Serialization error simulation",
            "ERROR",
            "ERROR",
            sqlState);
    }

    [Fact]
    public async Task Two40001FailuresThenSuccess_SucceedsAfter3Invocations()
    {
        var sp = CreateServiceProvider();
        using var scope = sp.CreateScope();
        var behavior = new SerializationRetryBehavior<ProbeRequest, string>(scope.ServiceProvider);

        var invocations = 0;
        RequestHandlerDelegate<string> next = () =>
        {
            invocations++;
            if (invocations <= 2)
            {
                throw CreatePostgresException("40001");
            }
            return Task.FromResult("success");
        };

        var result = await behavior.Handle(new ProbeRequest("test"), next, CancellationToken.None);

        result.Should().Be("success");
        invocations.Should().Be(3);
    }

    [Fact]
    public async Task Continuous40001Failures_ThrowsDomainExceptionSerializationConflictAfter4TotalInvocations()
    {
        var sp = CreateServiceProvider();
        using var scope = sp.CreateScope();
        var behavior = new SerializationRetryBehavior<ProbeRequest, string>(scope.ServiceProvider);

        var invocations = 0;
        RequestHandlerDelegate<string> next = () =>
        {
            invocations++;
            throw CreatePostgresException("40001");
        };

        var act = () => behavior.Handle(new ProbeRequest("test"), next, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be("serialization_conflict");
        invocations.Should().Be(4); // 1 initial + 3 retries
    }

    [Fact]
    public async Task OrdinaryException_IsNotRetried_FailsAfter1Invocation()
    {
        var sp = CreateServiceProvider();
        using var scope = sp.CreateScope();
        var behavior = new SerializationRetryBehavior<ProbeRequest, string>(scope.ServiceProvider);

        var invocations = 0;
        RequestHandlerDelegate<string> next = () =>
        {
            invocations++;
            throw new InvalidOperationException("Ordinary error");
        };

        var act = () => behavior.Handle(new ProbeRequest("test"), next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        invocations.Should().Be(1);
    }

    [Fact]
    public async Task ReadOnlyRequest_IsNotRetried_FailsAfter1Invocation()
    {
        var sp = CreateServiceProvider();
        using var scope = sp.CreateScope();
        var behavior = new SerializationRetryBehavior<ProbeReadOnlyRequest, string>(scope.ServiceProvider);

        var invocations = 0;
        RequestHandlerDelegate<string> next = () =>
        {
            invocations++;
            throw CreatePostgresException("40001");
        };

        var act = () => behavior.Handle(new ProbeReadOnlyRequest("test"), next, CancellationToken.None);

        await act.Should().ThrowAsync<PostgresException>();
        invocations.Should().Be(1);
    }

    [Fact]
    public async Task WrappedInDbUpdateException_IsStillRecognisedAndRetried()
    {
        var sp = CreateServiceProvider();
        using var scope = sp.CreateScope();
        var behavior = new SerializationRetryBehavior<ProbeRequest, string>(scope.ServiceProvider);

        var invocations = 0;
        RequestHandlerDelegate<string> next = () =>
        {
            invocations++;
            if (invocations <= 2)
            {
                var pgEx = CreatePostgresException("40P01");
                throw new DbUpdateException("DB update wrapper", pgEx);
            }
            return Task.FromResult("recovered");
        };

        var result = await behavior.Handle(new ProbeRequest("test"), next, CancellationToken.None);

        result.Should().Be("recovered");
        invocations.Should().Be(3);
    }
}
