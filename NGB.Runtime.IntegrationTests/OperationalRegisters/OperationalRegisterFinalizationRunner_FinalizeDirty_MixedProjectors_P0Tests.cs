using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.OperationalRegisters.Projections;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: FinalizeDirtyAsync (across all registers) must:
/// - finalize months for registers with a projector,
/// - use the default projector for registers without a custom projector,
/// - respect maxItems.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterFinalizationRunner_FinalizeDirty_MixedProjectors_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task FinalizeDirtyAsync_WhenSomeRegistersHaveNoCustomProjector_UsesDefaultProjector_AndExplicitProjectorStillWins()
    {
        var callLog = new TestCallLog();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton(callLog);
                services.AddScoped<IOperationalRegisterMonthProjector, RrProjector>();
            });

        var withProjector = Guid.CreateVersion7();
        var withoutProjector = Guid.CreateVersion7();

        await SeedRegisterAsync(host, withProjector, code: "RR", name: "Rent Roll");
        await SeedRegisterAsync(host, withoutProjector, code: "NO_PROJECTOR", name: "No Projector");

        var jan = new DateOnly(2026, 1, 1);

        // Make ordering deterministic: ensure the no-projector item is first.
        await MarkDirtyAtAsync(host, withoutProjector, jan, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await MarkDirtyAtAsync(host, withProjector, jan, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>();

            var finalized = await runner.FinalizeDirtyAsync(maxItems: 50, manageTransaction: true, ct: CancellationToken.None);
            finalized.Should().Be(2, "registers without a custom projector should use the default projector");

            var finalized2 = await runner.FinalizeDirtyAsync(maxItems: 50, manageTransaction: true, ct: CancellationToken.None);
            finalized2.Should().Be(0, "all dirty items were finalized on the first pass");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            var a = await repo.GetAsync(withProjector, jan, CancellationToken.None);
            a.Should().NotBeNull();
            a!.Status.Should().Be(OperationalRegisterFinalizationStatus.Finalized);
            a.FinalizedAtUtc.Should().NotBeNull();
            a.DirtySinceUtc.Should().BeNull();

            var b = await repo.GetAsync(withoutProjector, jan, CancellationToken.None);
            b.Should().NotBeNull();
            b!.Status.Should().Be(OperationalRegisterFinalizationStatus.Finalized);
            b.FinalizedAtUtc.Should().NotBeNull();
            b.DirtySinceUtc.Should().BeNull();
        }

        callLog.Calls.Should().HaveCount(1);
        callLog.Calls[0].RegisterId.Should().Be(withProjector);
        callLog.Calls[0].PeriodMonth.Should().Be(jan);
    }

    [Fact]
    public async Task FinalizeDirtyAsync_RespectsMaxItems_AndLeavesRemainingDirty()
    {
        var callLog = new TestCallLog();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton(callLog);
                services.AddScoped<IOperationalRegisterMonthProjector, RrProjector>();
            });

        var registerId = Guid.CreateVersion7();
        await SeedRegisterAsync(host, registerId, code: "RR", name: "Rent Roll");

        var jan = new DateOnly(2026, 1, 1);
        var feb = new DateOnly(2026, 2, 1);
        var mar = new DateOnly(2026, 3, 1);

        await MarkDirtyAtAsync(host, registerId, jan, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await MarkDirtyAtAsync(host, registerId, feb, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        await MarkDirtyAtAsync(host, registerId, mar, new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>();

            var finalized = await runner.FinalizeDirtyAsync(maxItems: 2, manageTransaction: true, ct: CancellationToken.None);
            finalized.Should().Be(2);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            (await repo.GetAsync(registerId, jan, CancellationToken.None))!.Status.Should().Be(OperationalRegisterFinalizationStatus.Finalized);
            (await repo.GetAsync(registerId, feb, CancellationToken.None))!.Status.Should().Be(OperationalRegisterFinalizationStatus.Finalized);

            var marRow = await repo.GetAsync(registerId, mar, CancellationToken.None);
            marRow.Should().NotBeNull();
            marRow!.Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty, "items above maxItems must remain dirty");
            marRow.DirtySinceUtc.Should().NotBeNull();
        }

        callLog.Calls.Should().HaveCount(2);
        callLog.Calls.Select(x => x.PeriodMonth).Should().Equal(jan, feb);
    }

    private static async Task SeedRegisterAsync(IHost host, Guid registerId, string code, string name)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);
        await repo.UpsertAsync(new OperationalRegisterUpsert(registerId, code, name), nowUtc, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);
    }

    private static async Task MarkDirtyAtAsync(IHost host, Guid registerId, DateOnly periodMonth, DateTime dirtySinceUtc)
    {
        if (periodMonth.Day != 1)
            throw new NgbArgumentInvalidException(nameof(periodMonth), "periodMonth must be a month start (day=1)");

        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

        await uow.BeginTransactionAsync(CancellationToken.None);
        await repo.MarkDirtyAsync(registerId, periodMonth, dirtySinceUtc, nowUtc: dirtySinceUtc, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);
    }

    private sealed class TestCallLog
    {
        private readonly ConcurrentQueue<Call> _calls = new();

        public IReadOnlyList<Call> Calls => _calls.ToArray();

        public void Add(Guid registerId, DateOnly periodMonth)
            => _calls.Enqueue(new Call(registerId, periodMonth));

        public sealed record Call(Guid RegisterId, DateOnly PeriodMonth);
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

            log.Add(context.RegisterId, context.PeriodMonth);
        }
    }
}
