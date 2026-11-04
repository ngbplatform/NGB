using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.IntegrationEvents;
using NGB.Contracts.Metadata;
using NGB.Persistence.Outbox;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using Testcontainers.PostgreSql;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class OutboxDeliveryTests
{
    [Fact]
    public async Task Failed_delivery_survives_processor_restart_and_stops_after_eight_attempts()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        Assert.Equal(0, await Migrator.Program.Main(["--connection=" + postgres.GetConnectionString()]));
        var clock = new DeliveryClock();
        var policy = new DeliveryPolicy { Fail = true };
        await using (var services = Services(postgres, clock, policy))
        {
            await AppendAsync(services, [Envelope(clock)]);
        }

        for (var attempt = 1; attempt <= 8; attempt++)
        {
            // A fresh runtime container models a restarted processor; only PostgreSQL retains delivery state.
            await using var services = Services(postgres, clock, policy);
            Assert.Equal(1, await ProcessAsync(services, 25));
            Assert.Equal(attempt, policy.Attempts);
            var health = await HealthAsync(services);
            Assert.Equal(1, health.FailedCount);
            Assert.Equal(attempt < 8 ? 1 : 0, health.PendingCount);
            Assert.Equal(attempt < 8, health.OldestOccurredAtUtc.HasValue);
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(0, await ProcessAsync(services, 25));
            clock.Advance(TimeSpan.FromMinutes(16));
        }

        policy.Fail = false;
        clock.Advance(TimeSpan.FromDays(1));
        await using var restarted = Services(postgres, clock, policy);
        Assert.Equal(0, await ProcessAsync(restarted, 25));
        Assert.Equal(8, policy.Attempts);
        Assert.Equal(1, (await HealthAsync(restarted)).FailedCount);
    }

    [Fact]
    public async Task Batch_size_is_bounded_and_expired_claims_reject_the_previous_attempt()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        Assert.Equal(0, await Migrator.Program.Main(["--connection=" + postgres.GetConnectionString()]));
        var clock = new DeliveryClock();
        var policy = new DeliveryPolicy();
        await using var services = Services(postgres, clock, policy);
        await AppendAsync(services, Enumerable.Range(0, 26).Select(_ => Envelope(clock)).ToArray());
        Assert.Equal(25, await ProcessAsync(services, int.MaxValue));
        Assert.Equal(1, (await HealthAsync(services)).PendingCount);
        Assert.Equal(1, await ProcessAsync(services, 25));
        Assert.Equal(26, policy.Attempts);
        Assert.Equal(0, await ProcessAsync(services, 25));

        var events = new[] { Envelope(clock), Envelope(clock) };
        await AppendAsync(services, events);
        var claims = await Task.WhenAll(ClaimAsync(services, clock), ClaimAsync(services, clock));
        Assert.Equal(events.Select(item => item.EventId).Order(), claims.Select(item => item.Event.EventId).Order());
        Assert.All(claims, item => Assert.Equal(1, item.AttemptCount));
        Assert.Equal(0, await ProcessAsync(services, 25));

        clock.Advance(TimeSpan.FromMinutes(31));
        var reclaimed = await Task.WhenAll(ClaimAsync(services, clock), ClaimAsync(services, clock));
        Assert.Equal(events.Select(item => item.EventId).Order(), reclaimed.Select(item => item.Event.EventId).Order());
        Assert.All(reclaimed, item => Assert.Equal(2, item.AttemptCount));
        foreach (var item in reclaimed)
        {
            await using var scope = services.CreateAsyncScope();
            var transaction = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repository = scope.ServiceProvider.GetRequiredService<IOutboxEventRepository>();
            await Assert.ThrowsAsync<NgbInvariantViolationException>(() => transaction.ExecuteInUowTransactionAsync(ct =>
                repository.MarkCompletedAsync(item.Event.EventId, "work-center", 1, clock.GetUtcNow().UtcDateTime, ct)));
            await transaction.ExecuteInUowTransactionAsync(ct =>
                repository.MarkCompletedAsync(item.Event.EventId, "work-center", 2, clock.GetUtcNow().UtcDateTime, ct));
        }
        Assert.Equal(0, await ProcessAsync(services, 25));
        var finished = await HealthAsync(services);
        Assert.Equal(0, finished.PendingCount);
        Assert.Equal(0, finished.FailedCount);
    }

    private static ServiceProvider Services(PostgreSqlContainer postgres, DeliveryClock clock, DeliveryPolicy policy)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNgbRuntime().AddNgbPostgres(postgres.GetConnectionString());
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IDocumentActionCompletedWorkCenterPolicy>(policy);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static OutboxEventEnvelope Envelope(DeliveryClock clock)
    {
        var document = Guid.CreateVersion7();
        var now = clock.GetUtcNow().UtcDateTime;
        var completed = new DocumentActionCompletedV1(Guid.CreateVersion7(), now, "equipment-delivery-test",
            "document:" + document, null, Guid.CreateVersion7(), null,
            new DocumentActionCompletedDataV1(document, "example.delivery-probe", "unpost",
                DocumentStatus.Posted, DocumentStatus.Draft, 2));
        return new OutboxEventEnvelope(completed.EventId, completed.Type, completed.PayloadSchemaVersion,
            now, completed.Source, completed.Subject, null, completed.CorrelationId, null,
            JsonSerializer.Serialize(completed, Json), now);
    }

    private static async Task AppendAsync(ServiceProvider services, IReadOnlyList<OutboxEventEnvelope> events)
    {
        await using var scope = services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOutboxEventRepository>();
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInUowTransactionAsync(async ct =>
        {
            foreach (var envelope in events)
                await repository.AppendAsync(envelope, ["work-center"], ct);
        });
    }

    private static async Task<int> ProcessAsync(ServiceProvider services, int batchSize)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOutboxProcessor>().ProcessBatchAsync(batchSize, CancellationToken.None);
    }

    private static async Task<OutboxConsumerWorkItem> ClaimAsync(ServiceProvider services, DeliveryClock clock)
    {
        await using var scope = services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOutboxEventRepository>();
        var claimed = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInUowTransactionAsync(ct =>
            repository.ClaimBatchAsync("work-center", 1, clock.GetUtcNow().UtcDateTime, ct));
        return Assert.Single(claimed);
    }

    private static async Task<(long PendingCount, DateTime? OldestOccurredAtUtc, long FailedCount)> HealthAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOutboxEventRepository>().GetHealthAsync("work-center", CancellationToken.None);
    }

    private sealed class DeliveryClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration)
        {
            _now = _now.Add(duration);
        }
    }

    private sealed class DeliveryPolicy : IDocumentActionCompletedWorkCenterPolicy
    {
        public bool Fail { get; set; }
        public int Attempts;

        public Task<IReadOnlyList<Guid>> HandleAsync(DocumentActionCompletedV1 @event, CancellationToken ct)
        {
            Interlocked.Increment(ref Attempts);
            if (Fail)
                throw new InvalidOperationException("Persistent delivery probe failure.");
            return Task.FromResult<IReadOnlyList<Guid>>([]);
        }
    }
}
