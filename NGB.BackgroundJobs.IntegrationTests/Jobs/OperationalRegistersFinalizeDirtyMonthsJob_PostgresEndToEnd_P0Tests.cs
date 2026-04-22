using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.IntegrationTests.Infrastructure;
using NGB.BackgroundJobs.Jobs;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.OperationalRegisters.Projections;
using Xunit;

namespace NGB.BackgroundJobs.IntegrationTests.Jobs;

[Collection(HangfirePostgresCollection.Name)]
public sealed class OperationalRegistersFinalizeDirtyMonthsJob_PostgresEndToEnd_P0Tests(HangfirePostgresFixture fixture)
{
    [Fact]
    public async Task RunAsync_FinalizesUpTo50DirtyMonths_AndLeavesRemainingDirty()
    {
        var callLog = new TestCallLog();

        using var sp = BuildServiceProvider(fixture.ConnectionString, callLog);

        Guid registerId;

        // 1) Create register with code_norm=rr to match the test projector.
        await using (var scope = sp.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
            registerId = await mgmt.UpsertAsync(code: "RR", name: "BGJ finalize dirty months test", ct: CancellationToken.None);
            registerId.Should().NotBe(Guid.Empty);
        }

        // 2) Seed 60 dirty months in a single transaction, with deterministic DirtySinceUtc ordering.
        var month0 = new DateOnly(2026, 1, 1);
        var dirtySince0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await using (var scope = sp.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            for (var i = 0; i < 60; i++)
            {
                var period = month0.AddMonths(i);
                var t = dirtySince0.AddMinutes(i);

                await repo.MarkDirtyAsync(
                    registerId,
                    period,
                    dirtySinceUtc: t,
                    nowUtc: t,
                    CancellationToken.None);
            }

            await uow.CommitAsync(CancellationToken.None);
        }

        // 3) Run the job (bounded maxItems=50).
        var metrics = new TestJobRunMetrics();

        await using (var scope = sp.CreateAsyncScope())
        {
            var maintenance = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminMaintenanceService>();

            var job = new OperationalRegistersFinalizeDirtyMonthsJob(
                maintenance,
                NullLogger<OperationalRegistersFinalizeDirtyMonthsJob>.Instance,
                metrics);

            await job.RunAsync(CancellationToken.None);
        }

        var snapshot = metrics.Snapshot();
        snapshot["max_items"].Should().Be(50);
        snapshot["finalized_count"].Should().Be(50);

        // 4) Verify: first 50 months finalized; remaining 10 still Dirty.
        await using (var scope = sp.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            for (var i = 0; i < 60; i++)
            {
                var period = month0.AddMonths(i);
                var row = await repo.GetAsync(registerId, period, CancellationToken.None);

                row.Should().NotBeNull();

                if (i < 50)
                    row!.Status.Should().Be(OperationalRegisterFinalizationStatus.Finalized);
                else
                    row!.Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);
            }
        }

        callLog.Calls.Should().HaveCount(50);
        callLog.Calls[0].Should().Be(month0);
        callLog.Calls[49].Should().Be(month0.AddMonths(49));
    }

    private static ServiceProvider BuildServiceProvider(string connectionString, TestCallLog callLog)
    {
        var services = new ServiceCollection();

        services.AddLogging();

        // Test projector for code_norm=rr.
        services.AddSingleton(callLog);
        services.AddScoped<IOperationalRegisterMonthProjector, RrProjector>();

        services.AddNgbPostgres(connectionString);
        services.AddNgbRuntime();

        return services.BuildServiceProvider();
    }

    private sealed class TestJobRunMetrics : IJobRunMetrics
    {
        private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);

        public void Increment(string name, long delta = 1)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;
            if (delta == 0)
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

    private sealed class TestCallLog
    {
        private readonly ConcurrentQueue<DateOnly> _calls = new();

        public IReadOnlyList<DateOnly> Calls => _calls.ToArray();

        public void Add(DateOnly periodMonth) => _calls.Enqueue(periodMonth);
    }

    private sealed class RrProjector(
        TestCallLog log,
        IUnitOfWork uow,
        IOperationalRegisterFinalizationRepository finalizations)
        : IOperationalRegisterMonthProjector
    {
        public string RegisterCodeNorm => "rr";

        public async Task RebuildMonthAsync(OperationalRegisterMonthProjectionContext context, CancellationToken ct = default)
        {
            // Runner must execute projector inside the same transaction.
            uow.EnsureActiveTransaction();

            context.RegisterCodeNorm.Should().Be("rr");

            var current = await finalizations.GetAsync(context.RegisterId, context.PeriodMonth, ct);
            current.Should().NotBeNull();
            current!.Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);

            log.Add(context.PeriodMonth);
        }
    }
}
