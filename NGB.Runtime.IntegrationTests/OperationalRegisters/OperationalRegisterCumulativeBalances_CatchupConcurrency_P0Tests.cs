using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Locks;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Locks;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.OperationalRegisters;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

public sealed partial class OperationalRegisterCumulativeBalances_DefaultProjector_P0Tests
{
    private static readonly DateOnly January = new(2026, 1, 1);
    private static readonly DateTime JanuaryDate = new(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(OperationalRegisterWriteOperation.Post, 150)]
    [InlineData(OperationalRegisterWriteOperation.Repost, 100)]
    [InlineData(OperationalRegisterWriteOperation.Unpost, 20)]
    public async Task Preparation_catches_up_Post_Repost_Unpost_without_restarting_or_publishing_stale_balances(
        OperationalRegisterWriteOperation operation, decimal expected)
    {
        var gate = new PreparationGate();
        using var host = GatedHost(gate);
        var (registerId, documentId) = await SeedProjectionAsync(host);
        var newDocumentId = Guid.CreateVersion7();
        await SeedDocumentAsync(host, newDocumentId, JanuaryDate);
        var pendingDocumentId = Guid.CreateVersion7();
        await SeedDocumentAsync(host, pendingDocumentId, JanuaryDate);
        await ApplyPostAsync(host, registerId, pendingDocumentId, CreateMovement(pendingDocumentId, JanuaryDate, 20m));
        await MarkMonthDirtyAsync(host, registerId, January.AddMonths(1));

        gate.Arm();
        var running = FinalizeDirtyAsync(host, registerId);
        await gate.Prepared.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            // Model posting's lock order: accounting first, then operational movements.
            // It must commit while the finalizer still owns its uncommitted projection rows.
            await using var writer = host.Services.CreateAsyncScope();
            var uow = writer.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var locks = writer.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();
            var applier = writer.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await uow.BeginTransactionAsync(deadline.Token);
            await locks.LockPeriodAsync(January, deadline.Token);
            var id = operation == OperationalRegisterWriteOperation.Post ? newDocumentId : documentId;
            var movements = operation == OperationalRegisterWriteOperation.Unpost
                ? Array.Empty<OperationalRegisterMovement>()
                : new[] { CreateMovement(id, JanuaryDate, operation == OperationalRegisterWriteOperation.Post ? 30m : 80m) };
            (await applier.ApplyMovementsForDocumentAsync(registerId, id, operation, movements,
                affectedPeriods: new[] { January }, manageTransaction: false, ct: deadline.Token))
                .Should().Be(OperationalRegisterWriteResult.Executed);
            await uow.CommitAsync(deadline.Token);
            (await ReadBalanceAsync(host, registerId)).Should().Be(100m, "uncommitted preparation must remain invisible");
        }
        finally { gate.Release.TrySetResult(); }

        (await running.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(2);
        await using (var check = host.Services.CreateAsyncScope())
        {
            var finalizations = check.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();
            (await finalizations.GetAsync(registerId, January))!.Status.Should().Be(OperationalRegisterFinalizationStatus.Finalized);
            (await finalizations.GetAsync(registerId, January.AddMonths(1)))!.Status.Should().Be(OperationalRegisterFinalizationStatus.Finalized);
        }
        (await FinalizeDirtyAsync(host, registerId)).Should().Be(0, "both months completed in the original pass");
        (await ReadBalanceAsync(host, registerId)).Should().Be(expected);
        (await ReadBalanceAsync(host, registerId, January.AddMonths(1))).Should().Be(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Busy_publication_locks_queue_and_complete_after_the_holder_commits(bool monthLock)
    {
        var gate = new PreparationGate();
        using var host = GatedHost(gate);
        var (registerId, documentId) = await SeedProjectionAsync(host);
        await ApplyRepostAsync(host, registerId, documentId, CreateMovement(documentId, JanuaryDate, 120m));
        gate.Arm();
        var running = FinalizeDirtyAsync(host, registerId);
        await gate.Prepared.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var blocker = host.Services.CreateAsyncScope();
        var uow = blocker.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var locks = blocker.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();
        await uow.BeginTransactionAsync();
        if (monthLock)
            await locks.LockPeriodAsync(January, AdvisoryLockPeriodScope.OperationalRegister);
        else
            await locks.LockOperationalRegisterAsync(registerId);
        gate.Release.TrySetResult();
        await gate.Completing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        running.IsCompleted.Should().BeFalse();
        (await ReadBalanceAsync(host, registerId)).Should().Be(100m);
        await uow.RollbackAsync();
        (await running.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(1);
        (await ReadBalanceAsync(host, registerId)).Should().Be(120m);
    }

    [Fact]
    public async Task Cancellation_after_preparation_rolls_back_and_releases_finalizer_locks()
    {
        var gate = new PreparationGate();
        using var host = GatedHost(gate);
        var (registerId, documentId) = await SeedProjectionAsync(host);
        await ApplyRepostAsync(host, registerId, documentId, CreateMovement(documentId, JanuaryDate, 120m));
        gate.Arm();
        using var cancellation = new CancellationTokenSource();
        await using var scope = host.Services.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>();
        var running = runner.FinalizeRegisterDirtyAsync(registerId, ct: cancellation.Token);
        await gate.Prepared.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await ((Func<Task>)(async () => await running.WaitAsync(TimeSpan.FromSeconds(5))))
            .Should().ThrowAsync<OperationCanceledException>();
        scope.ServiceProvider.GetRequiredService<IUnitOfWork>().HasActiveTransaction.Should().BeFalse();
        (await ReadBalanceAsync(host, registerId)).Should().Be(100m);
        (await FinalizeDirtyAsync(host, registerId)).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_default_finalizers_publish_once_and_keep_unchanged_projection_row_versions()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (registerId, _) = await SeedProjectionAsync(host);
        var before = await ProjectionVersionsAsync(host, registerId);
        await MarkMonthDirtyAsync(host, registerId, January);
        var counts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => FinalizeDirtyAsync(host, registerId)))
            .WaitAsync(TimeSpan.FromSeconds(20));
        counts.Sum().Should().Be(1);
        (await ProjectionVersionsAsync(host, registerId)).Should().Equal(before,
            "a rebuild with unchanged values must not delete/reinsert or update projection rows");
    }

    [Fact]
    public async Task A_write_between_months_is_included_in_the_next_snapshot_even_when_the_predecessor_is_dirty()
    {
        var gate = new PreparationGate();
        using var host = GatedHost(gate);
        var (registerId, januaryDocument) = await SeedProjectionAsync(host);
        var february = January.AddMonths(1);
        var februaryDocument = Guid.CreateVersion7();
        var date = JanuaryDate.AddMonths(1);
        await SeedDocumentAsync(host, februaryDocument, date);
        await ApplyPostAsync(host, registerId, februaryDocument, CreateMovement(februaryDocument, date, 50m));
        (await FinalizeDirtyAsync(host, registerId)).Should().Be(1);
        await MarkMonthDirtyAsync(host, registerId, January);
        gate.BeforePrepare = async month =>
        {
            if (month != february) return;
            gate.BeforePrepare = null;
            await ApplyRepostAsync(host, registerId, januaryDocument, CreateMovement(januaryDocument, JanuaryDate, 80m));
        };

        (await FinalizeDirtyAsync(host, registerId)).Should().Be(2);
        (await ReadBalanceAsync(host, registerId, february)).Should().Be(130m,
            "February must include the backdated write, not skip a dirty predecessor or lose January history");
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();
            (await repository.GetAsync(registerId, January))!.Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);
            (await repository.GetAsync(registerId, february))!.Status.Should().Be(OperationalRegisterFinalizationStatus.Finalized);
        }
        (await FinalizeDirtyAsync(host, registerId)).Should().Be(1);
        (await ReadBalanceAsync(host, registerId, february)).Should().Be(130m);
    }

    private IHost GatedHost(PreparationGate gate, Action<IServiceCollection>? configure = null)
        => IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.AddScoped<IOperationalRegisterDefaultProjectionRebuilder>(sp =>
                new GatedRebuilder(ActivatorUtilities.CreateInstance<PostgresOperationalRegisterDefaultProjectionRebuilder>(sp), gate));
            configure?.Invoke(services);
        });

    private static async Task<(Guid RegisterId, Guid DocumentId)> SeedProjectionAsync(IHost host)
    {
        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        await SeedRegisterAsync(host, registerId, "it_catchup_" + registerId.ToString("N"),
            new[] { new OperationalRegisterResourceDefinition("amount", "Amount", 1) });
        await SeedDocumentAsync(host, documentId, JanuaryDate);
        await ApplyPostAsync(host, registerId, documentId, CreateMovement(documentId, JanuaryDate, 100m));
        (await FinalizeDirtyAsync(host, registerId)).Should().Be(1);
        return (registerId, documentId);
    }

    private static async Task MarkMonthDirtyAsync(IHost host, Guid registerId, DateOnly month)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationService>().MarkDirtyAsync(registerId, month);
    }

    private static async Task<decimal> ReadBalanceAsync(IHost host, Guid registerId, DateOnly? month = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>()
            .GetByMonthAsync(registerId, month ?? January);
        return rows.Sum(row => row.Values["amount"]);
    }

    private static async Task<string[]> ProjectionVersionsAsync(IHost host, Guid registerId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var register = await scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>().GetByIdAsync(registerId);
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync();
        var tableCode = register!.TableCode;
        var turnovers = OperationalRegisterNaming.TurnoversTable(tableCode);
        var balances = OperationalRegisterNaming.BalancesTable(tableCode);
        return (await uow.Connection.QueryAsync<string>($"SELECT xmin::text FROM {turnovers} UNION ALL SELECT xmin::text FROM {balances}"))
            .ToArray();
    }

    private sealed class PreparationGate
    {
        public int Armed;
        public int PrepareCount;
        public TaskCompletionSource Completing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<DateOnly, Task>? BeforePrepare { get; set; }
        public TaskCompletionSource Prepared { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() => Interlocked.Exchange(ref Armed, 1);
    }

    private sealed class GatedPrepared(IOperationalRegisterPreparedProjection inner, PreparationGate gate)
        : IOperationalRegisterPreparedProjection
    {
        public async Task CompleteAsync(CancellationToken ct = default)
        {
            gate.Completing.TrySetResult();
            await inner.CompleteAsync(ct);
        }
    }

    private sealed class GatedRebuilder(PostgresOperationalRegisterDefaultProjectionRebuilder inner, PreparationGate gate)
        : IOperationalRegisterDefaultProjectionRebuilder, IOperationalRegisterProjectionPreparation
    {
        public Task RebuildMonthAsync(Guid registerId, DateOnly month, DateOnly? previous, CancellationToken ct = default)
            => inner.RebuildMonthAsync(registerId, month, previous, ct);

        public async Task<IOperationalRegisterPreparedProjection> PrepareMonthAsync(Guid registerId, DateOnly month, CancellationToken ct = default)
        {
            Interlocked.Increment(ref gate.PrepareCount);
            if (gate.BeforePrepare is { } before) await before(month);
            var prepared = await inner.PrepareMonthAsync(registerId, month, ct);
            if (Interlocked.Exchange(ref gate.Armed, 0) == 1)
            {
                gate.Prepared.TrySetResult();
                await gate.Release.Task.WaitAsync(ct);
                return new GatedPrepared(prepared, gate);
            }
            return prepared;
        }
    }
}
