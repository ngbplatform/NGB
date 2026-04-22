using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterFinalizationRunner_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string TxnRequired = "This operation requires an active transaction.";

    [Fact]
    public async Task FinalizeRegisterDirty_WhenProjectorRegistered_InvokesProjectorAndMarksFinalized()
    {
        var callLog = new TestFinalizerCallLog();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton(callLog);
                services.AddScoped<IOperationalRegisterMonthProjector, TestOperationalRegisterMonthProjector>();
            });

        var registerId = Guid.CreateVersion7();
        await SeedRegisterAsync(host, registerId, code: "RR", name: "Rent Roll");

        await MarkDirtyAsync(host, registerId, new DateOnly(2026, 1, 15));
        await MarkDirtyAsync(host, registerId, new DateOnly(2026, 2, 20));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>();

            var finalized = await runner.FinalizeRegisterDirtyAsync(
                registerId,
                maxPeriods: 50,
                manageTransaction: true,
                ct: CancellationToken.None);

            finalized.Should().Be(2);
        }

        var jan = new DateOnly(2026, 1, 1);
        var feb = new DateOnly(2026, 2, 1);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            var janRow = await repo.GetAsync(registerId, jan, CancellationToken.None);
            janRow.Should().NotBeNull();
            janRow!.Status.Should().Be(OperationalRegisterFinalizationStatus.Finalized);
            janRow.FinalizedAtUtc.Should().NotBeNull();
            janRow.DirtySinceUtc.Should().BeNull();

            var febRow = await repo.GetAsync(registerId, feb, CancellationToken.None);
            febRow.Should().NotBeNull();
            febRow!.Status.Should().Be(OperationalRegisterFinalizationStatus.Finalized);
            febRow.FinalizedAtUtc.Should().NotBeNull();
            febRow.DirtySinceUtc.Should().BeNull();
        }

        callLog.Calls.Should().HaveCount(2);
        callLog.Calls.Select(c => c.RegisterId).Should().AllBeEquivalentTo(registerId);
        callLog.Calls.Select(c => c.PeriodMonth).Should().Equal(jan, feb);
    }

    [Fact]
    public async Task FinalizeRegisterDirty_WhenNoCustomProjectorRegistered_UsesDefaultProjector_AndMarksFinalized()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var code = "RR_DEFAULT_" + Guid.CreateVersion7().ToString("N")[..10].ToUpperInvariant();

        await SeedRegisterAsync(host, registerId, code, "Rent Roll", resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 1)
        });

        await AppendMovementAsync(
            host,
            registerId,
            new OperationalRegisterMovement(
                Guid.CreateVersion7(),
                new DateTime(2026, 3, 5, 12, 0, 0, DateTimeKind.Utc),
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["amount"] = 10m
                }));

        await MarkDirtyAsync(host, registerId, new DateOnly(2026, 3, 5));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>();

            var finalized = await runner.FinalizeRegisterDirtyAsync(
                registerId,
                maxPeriods: 50,
                manageTransaction: true,
                ct: CancellationToken.None);

            finalized.Should().Be(1);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();
            var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
            var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

            var mar = await repo.GetAsync(registerId, new DateOnly(2026, 3, 1), CancellationToken.None);
            mar.Should().NotBeNull();
            mar!.Status.Should().Be(OperationalRegisterFinalizationStatus.Finalized);
            mar.FinalizedAtUtc.Should().NotBeNull();
            mar.DirtySinceUtc.Should().BeNull();

            var turnoverRows = await turnovers.GetByMonthAsync(registerId, new DateOnly(2026, 3, 1), ct: CancellationToken.None);
            var balanceRows = await balances.GetByMonthAsync(registerId, new DateOnly(2026, 3, 1), ct: CancellationToken.None);

            turnoverRows.Should().ContainSingle();
            balanceRows.Should().ContainSingle();
            turnoverRows[0].Values["amount"].Should().Be(10m);
            balanceRows[0].Values["amount"].Should().Be(10m);
        }
    }

    [Fact]
    public async Task FinalizeRegisterDirty_WhenDefaultProjectorRemoved_DoesNotFinalize_AndMarksBlockedNoProjector()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.RemoveAll<IOperationalRegisterDefaultMonthProjector>());

        var registerId = Guid.CreateVersion7();
        await SeedRegisterAsync(host, registerId, code: "RR", name: "Rent Roll");
        await MarkDirtyAsync(host, registerId, new DateOnly(2026, 3, 5));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>();

            var finalized = await runner.FinalizeRegisterDirtyAsync(
                registerId,
                maxPeriods: 50,
                manageTransaction: true,
                ct: CancellationToken.None);

            finalized.Should().Be(0);

            // Second call should not retry the same month again (anti-spam behavior).
            var finalized2 = await runner.FinalizeRegisterDirtyAsync(
                registerId,
                maxPeriods: 50,
                manageTransaction: true,
                ct: CancellationToken.None);

            finalized2.Should().Be(0);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            var mar = await repo.GetAsync(registerId, new DateOnly(2026, 3, 1), CancellationToken.None);
            mar.Should().NotBeNull();
            mar!.Status.Should().Be(OperationalRegisterFinalizationStatus.BlockedNoProjector);
            mar.FinalizedAtUtc.Should().BeNull();
            mar.DirtySinceUtc.Should().BeNull();
        }
    }

    [Fact]
    public async Task FinalizeRegisterDirty_ManageTransactionFalse_WithoutTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        await SeedRegisterAsync(host, registerId, code: "RR", name: "Rent Roll");
        await MarkDirtyAsync(host, registerId, new DateOnly(2026, 4, 1));

        await using var scope = host.Services.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>();

        var act = async () => await runner.FinalizeRegisterDirtyAsync(
            registerId,
            maxPeriods: 50,
            manageTransaction: false,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage(TxnRequired);
    }

    [Fact]
    public async Task FinalizeRegisterDirty_ManageTransactionFalse_RespectsOuterRollback()
    {
        var callLog = new TestFinalizerCallLog();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton(callLog);
                services.AddScoped<IOperationalRegisterMonthProjector, TestOperationalRegisterMonthProjector>();
            });

        var registerId = Guid.CreateVersion7();
        await SeedRegisterAsync(host, registerId, code: "RR", name: "Rent Roll");
        await MarkDirtyAsync(host, registerId, new DateOnly(2026, 4, 18));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var runner = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            var finalized = await runner.FinalizeRegisterDirtyAsync(
                registerId,
                maxPeriods: 50,
                manageTransaction: false,
                ct: CancellationToken.None);

            // With manageTransaction:false the runner works inside the *current* transaction.
            // Even if the caller later rolls back, the runner still processed the dirty month
            // within that transaction.
            finalized.Should().Be(1);

            await uow.RollbackAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            var apr = await repo.GetAsync(registerId, new DateOnly(2026, 4, 1), CancellationToken.None);
            apr.Should().NotBeNull();
            apr!.Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);
            apr.FinalizedAtUtc.Should().BeNull();
            apr.DirtySinceUtc.Should().NotBeNull();
        }

        callLog.Calls.Should().HaveCount(1);
        callLog.Calls[0].PeriodMonth.Should().Be(new DateOnly(2026, 4, 1));
    }

    private static async Task SeedRegisterAsync(
        IHost host,
        Guid registerId,
        string code,
        string name,
        IReadOnlyList<OperationalRegisterResourceDefinition>? resources = null)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var resourcesRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);
        await repo.UpsertAsync(new OperationalRegisterUpsert(registerId, code, name), nowUtc, CancellationToken.None);
        if (resources is not null)
            await resourcesRepo.ReplaceAsync(registerId, resources, nowUtc, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);
    }

    private static async Task MarkDirtyAsync(IHost host, Guid registerId, DateOnly anyDateInMonth)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationService>();
        await svc.MarkDirtyAsync(registerId, anyDateInMonth, manageTransaction: true, ct: CancellationToken.None);
    }

    private static async Task AppendMovementAsync(
        IHost host,
        Guid registerId,
        OperationalRegisterMovement movement)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var store = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>();

        await uow.BeginTransactionAsync(CancellationToken.None);
        await store.EnsureSchemaAsync(registerId, CancellationToken.None);
        await store.AppendAsync(registerId, [movement], CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);
    }

    private sealed class TestFinalizerCallLog
    {
        private readonly ConcurrentQueue<TestFinalizerCall> _calls = new();

        public IReadOnlyList<TestFinalizerCall> Calls => _calls.ToArray();

        public void Add(Guid registerId, DateOnly periodMonth)
            => _calls.Enqueue(new TestFinalizerCall(registerId, periodMonth));

        public sealed record TestFinalizerCall(Guid RegisterId, DateOnly PeriodMonth);
    }

    private sealed class TestOperationalRegisterMonthProjector(
        TestFinalizerCallLog log,
        IUnitOfWork uow,
        IOperationalRegisterFinalizationRepository finalizations)
        : IOperationalRegisterMonthProjector
    {
        public string RegisterCodeNorm => "rr";

        public async Task RebuildMonthAsync(
            OperationalRegisterMonthProjectionContext context,
            CancellationToken ct = default)
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
