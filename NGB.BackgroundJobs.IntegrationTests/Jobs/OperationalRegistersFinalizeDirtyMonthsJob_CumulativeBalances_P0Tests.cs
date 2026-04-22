using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.IntegrationTests.Infrastructure;
using NGB.BackgroundJobs.Jobs;
using NGB.Core.Documents;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.BackgroundJobs.IntegrationTests.Jobs;

[Collection(HangfirePostgresCollection.Name)]
public sealed class OperationalRegistersFinalizeDirtyMonthsJob_CumulativeBalances_P0Tests(HangfirePostgresFixture fixture)
{
    [Fact]
    public async Task RunAsync_DefaultProjector_RebuildsCumulativeBalancesAfterHistoricalRepost()
    {
        using var sp = BuildServiceProvider(fixture.ConnectionString);
        await DrainDirtyQueueAsync(sp);

        var registerId = Guid.CreateVersion7();
        var registerCode = "bg_opreg_bal_" + Guid.CreateVersion7().ToString("N")[..8];
        var janDocId = Guid.CreateVersion7();
        var marDocId = Guid.CreateVersion7();

        await SeedRegisterAsync(sp, registerId, registerCode, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 1)
        });

        await SeedDocumentAsync(sp, janDocId, new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc));
        await SeedDocumentAsync(sp, marDocId, new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc));

        await ApplyMovementsAsync(sp, registerId, janDocId, OperationalRegisterWriteOperation.Post, CreateMovement(janDocId, new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc), 100m));
        await ApplyMovementsAsync(sp, registerId, marDocId, OperationalRegisterWriteOperation.Post, CreateMovement(marDocId, new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc), 50m));

        var firstRun = await RunJobAsync(sp);
        firstRun["max_items"].Should().Be(50);
        firstRun["finalized_count"].Should().Be(2);

        await AssertBalanceAsync(sp, registerId, new DateOnly(2026, 1, 1), 100m);
        await AssertBalanceAsync(sp, registerId, new DateOnly(2026, 3, 1), 150m);

        await ApplyMovementsAsync(sp, registerId, janDocId, OperationalRegisterWriteOperation.Repost, CreateMovement(janDocId, new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc), 80m));

        await using (var scope = sp.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            (await repo.GetAsync(registerId, new DateOnly(2026, 1, 1), CancellationToken.None))!
                .Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);

            (await repo.GetAsync(registerId, new DateOnly(2026, 3, 1), CancellationToken.None))!
                .Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);
        }

        var secondRun = await RunJobAsync(sp);
        secondRun["max_items"].Should().Be(50);
        secondRun["finalized_count"].Should().Be(2);

        await AssertBalanceAsync(sp, registerId, new DateOnly(2026, 1, 1), 80m);
        await AssertBalanceAsync(sp, registerId, new DateOnly(2026, 3, 1), 130m);
    }

    private static ServiceProvider BuildServiceProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNgbPostgres(connectionString);
        services.AddNgbRuntime();
        return services.BuildServiceProvider();
    }

    private static async Task<IReadOnlyDictionary<string, long>> RunJobAsync(ServiceProvider sp)
    {
        var metrics = new TestJobRunMetrics();

        await using var scope = sp.CreateAsyncScope();
        var maintenance = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminMaintenanceService>();
        var job = new OperationalRegistersFinalizeDirtyMonthsJob(
            maintenance,
            NullLogger<OperationalRegistersFinalizeDirtyMonthsJob>.Instance,
            metrics);

        await job.RunAsync(CancellationToken.None);
        return metrics.Snapshot();
    }

    private static async Task DrainDirtyQueueAsync(ServiceProvider sp)
    {
        while (true)
        {
            await using var scope = sp.CreateAsyncScope();
            var maintenance = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminMaintenanceService>();
            var finalized = await maintenance.FinalizeDirtyAsync(50, CancellationToken.None);
            if (finalized == 0)
                return;
        }
    }

    private static async Task AssertBalanceAsync(ServiceProvider sp, Guid registerId, DateOnly month, decimal expectedAmount)
    {
        await using var scope = sp.CreateAsyncScope();
        var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

        (await balances.GetByMonthAsync(registerId, month, ct: CancellationToken.None))
            .Single().Values["amount"].Should().Be(expectedAmount);
    }

    private static async Task ApplyMovementsAsync(
        ServiceProvider sp,
        Guid registerId,
        Guid documentId,
        OperationalRegisterWriteOperation operation,
        OperationalRegisterMovement movement)
    {
        await using var scope = sp.CreateAsyncScope();
        var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

        (await applier.ApplyMovementsForDocumentAsync(
            registerId,
            documentId,
            operation,
            [movement],
            affectedPeriods: null,
            manageTransaction: true,
            ct: CancellationToken.None)).Should().Be(OperationalRegisterWriteResult.Executed);
    }

    private static OperationalRegisterMovement CreateMovement(Guid documentId, DateTime occurredAtUtc, decimal amount)
        => new(
            documentId,
            occurredAtUtc,
            Guid.Empty,
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["amount"] = amount
            });

    private static async Task SeedRegisterAsync(
        ServiceProvider sp,
        Guid registerId,
        string code,
        IReadOnlyList<OperationalRegisterResourceDefinition> resources)
    {
        await using var scope = sp.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var resourceRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();
        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);
        await repo.UpsertAsync(new OperationalRegisterUpsert(registerId, code, "BG cumulative register"), nowUtc, CancellationToken.None);
        await resourceRepo.ReplaceAsync(registerId, resources, nowUtc, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);
    }

    private static async Task SeedDocumentAsync(ServiceProvider sp, Guid documentId, DateTime dateUtc)
    {
        await using var scope = sp.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        await uow.BeginTransactionAsync(CancellationToken.None);
        await docs.CreateAsync(
            new DocumentRecord
            {
                Id = documentId,
                TypeCode = "bg_it_doc",
                Number = "BG-" + documentId.ToString("N")[^12..],
                DateUtc = dateUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = dateUtc,
                UpdatedAtUtc = dateUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            },
            CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);
    }

    private sealed class TestJobRunMetrics : IJobRunMetrics
    {
        private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);

        public void Increment(string name, long delta = 1)
        {
            if (string.IsNullOrWhiteSpace(name) || delta == 0)
                return;

            name = name.Trim();
            _counters.TryGetValue(name, out var current);
            _counters[name] = current + delta;
        }

        public void Set(string name, long value)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            _counters[name.Trim()] = value;
        }

        public IReadOnlyDictionary<string, long> Snapshot() => new Dictionary<string, long>(_counters);
    }
}
