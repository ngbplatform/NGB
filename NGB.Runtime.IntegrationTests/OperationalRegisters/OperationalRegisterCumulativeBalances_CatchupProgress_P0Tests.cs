using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.Locks;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

public sealed partial class OperationalRegisterCumulativeBalances_DefaultProjector_P0Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Boundary_waits_for_an_already_allocated_movement_to_commit_or_roll_back(bool commit)
    {
        var gate = new PreparationGate();
        using var host = GatedHost(gate);
        var (registerId, _) = await SeedProjectionAsync(host);
        await MarkMonthDirtyAsync(host, registerId, January);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.BeforePrepare = _ => { entered.TrySetResult(); return Task.CompletedTask; };

        await using var writer = host.Services.CreateAsyncScope();
        var uow = writer.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.BeginTransactionAsync();
        await writer.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>()
            .AppendAsync(registerId, new[] { CreateMovement(Guid.CreateVersion7(), JanuaryDate, 25m) });
        var running = FinalizeDirtyAsync(host, registerId);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        running.IsCompleted.Should().BeFalse();
        if (commit) await uow.CommitAsync(); else await uow.RollbackAsync();
        (await running.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(1);
        (await ReadBalanceAsync(host, registerId)).Should().Be(commit ? 125m : 100m);

        // A rolled-back allocation leaves an ID gap. A later commit must still be included.
        var id = Guid.CreateVersion7();
        await SeedDocumentAsync(host, id, JanuaryDate);
        await ApplyPostAsync(host, registerId, id, CreateMovement(id, JanuaryDate, 7m));
        (await FinalizeDirtyAsync(host, registerId)).Should().Be(1);
        (await ReadBalanceAsync(host, registerId)).Should().Be(commit ? 132m : 107m);
    }

    [Fact]
    public async Task Finalization_completes_while_a_writer_keeps_committing_and_publishes_the_exact_ledger()
    {
        var gate = new PreparationGate();
        using var host = GatedHost(gate);
        var (registerId, _) = await SeedProjectionAsync(host);
        await MarkMonthDirtyAsync(host, registerId, January);
        var preparedBefore = gate.PrepareCount;
        gate.Arm();
        var running = FinalizeDirtyAsync(host, registerId);
        await gate.Prepared.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var firstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var committed = 0;
        var writer = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    await using var scope = host.Services.CreateAsyncScope();
                    var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    await uow.BeginTransactionAsync(stop.Token);
                    await scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>()
                        .AppendAsync(registerId, new[] { CreateMovement(Guid.CreateVersion7(), JanuaryDate, 0.125m) }, stop.Token);
                    await scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>()
                        .MarkDirtyAsync(registerId, January, DateTime.UtcNow, DateTime.UtcNow, stop.Token);
                    await uow.CommitAsync(stop.Token);
                    Interlocked.Increment(ref committed);
                    firstWrite.TrySetResult();
                    await Task.Delay(1, stop.Token);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        });
        try
        {
            await firstWrite.Task.WaitAsync(TimeSpan.FromSeconds(5));
            gate.Release.TrySetResult();
            (await running.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(1);
            writer.IsCompleted.Should().BeFalse("publication must complete without waiting for write traffic to stop");
            gate.PrepareCount.Should().Be(preparedBefore + 1, "new movements must not restart the full calculation");
        }
        finally { stop.Cancel(); gate.Release.TrySetResult(); await writer.WaitAsync(TimeSpan.FromSeconds(5)); }
        await FinalizeDirtyAsync(host, registerId);
        (await ReadBalanceAsync(host, registerId)).Should().Be(100m + committed * 0.125m);
        await AssertProjectionMatchesLedgerAsync(host, registerId, January);
    }

    [Theory]
    [InlineData(-1, 125, 100)]
    [InlineData(0, 125, 125)]
    [InlineData(1, 100, 100)]
    public async Task Tail_respects_month_boundaries_and_applies_backdated_changes_to_balances(
        int monthOffset, decimal balance, decimal turnover)
    {
        var gate = new PreparationGate();
        using var host = GatedHost(gate);
        var (registerId, _) = await SeedProjectionAsync(host);
        await MarkMonthDirtyAsync(host, registerId, January);
        var document = Guid.CreateVersion7();
        var date = JanuaryDate.AddMonths(monthOffset);
        await SeedDocumentAsync(host, document, date);
        gate.Arm();
        var running = FinalizeDirtyAsync(host, registerId);
        await gate.Prepared.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { await ApplyPostAsync(host, registerId, document, CreateMovement(document, date, 25m)); }
        finally { gate.Release.TrySetResult(); }
        (await running.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(1);
        (await ReadBalanceAsync(host, registerId)).Should().Be(balance);
        await using var scope = host.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>().GetByMonthAsync(registerId, January);
        rows.Single().Values["amount"].Should().Be(turnover);
        await AssertProjectionMatchesLedgerAsync(host, registerId, January);
    }

    [Fact]
    public async Task Tail_storno_prunes_zero_turnovers_and_balances_in_the_original_pass()
    {
        var gate = new PreparationGate();
        using var host = GatedHost(gate);
        var (registerId, documentId) = await SeedProjectionAsync(host);
        await MarkMonthDirtyAsync(host, registerId, January);
        gate.Arm();
        var running = FinalizeDirtyAsync(host, registerId);
        await gate.Prepared.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>()
                .ApplyMovementsForDocumentAsync(registerId, documentId, OperationalRegisterWriteOperation.Unpost, []);
        }
        finally { gate.Release.TrySetResult(); }
        (await running.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(1);
        await using var check = host.Services.CreateAsyncScope();
        (await check.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>().GetByMonthAsync(registerId, January)).Should().BeEmpty();
        (await check.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>().GetByMonthAsync(registerId, January)).Should().BeEmpty();
    }

    [Fact]
    public async Task Publication_exhaustion_is_a_failure_with_three_bounded_attempts_and_no_partial_commit()
    {
        var gate = new PreparationGate();
        using var host = GatedHost(gate, services => services.Configure<PostgresOptions>(o => o.OperationalRegisterPublicationTimeoutSeconds = 1));
        var (registerId, documentId) = await SeedProjectionAsync(host);
        await ApplyRepostAsync(host, registerId, documentId, CreateMovement(documentId, JanuaryDate, 120m));
        var before = gate.PrepareCount;
        gate.Arm();
        var running = FinalizeDirtyAsync(host, registerId);
        await gate.Prepared.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var blocker = host.Services.CreateAsyncScope();
        var uow = blocker.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.BeginTransactionAsync();
        await blocker.ServiceProvider.GetRequiredService<IAdvisoryLockManager>().LockOperationalRegisterAsync(registerId);
        gate.Release.TrySetResult();
        try
        {
            await ((Func<Task>)(async () => await running.WaitAsync(TimeSpan.FromSeconds(10))))
                .Should().ThrowAsync<OperationalRegisterFinalizationBusyException>();
            gate.PrepareCount.Should().Be(before + 3);
            (await ReadBalanceAsync(host, registerId)).Should().Be(100m);
        }
        finally { await uow.RollbackAsync(); }
        (await FinalizeDirtyAsync(host, registerId)).Should().Be(1);
        (await ReadBalanceAsync(host, registerId)).Should().Be(120m);
    }

    [Fact]
    public async Task Cancellation_while_waiting_for_publication_does_not_retry_or_commit()
    {
        var gate = new PreparationGate();
        using var host = GatedHost(gate);
        var (registerId, documentId) = await SeedProjectionAsync(host);
        await ApplyRepostAsync(host, registerId, documentId, CreateMovement(documentId, JanuaryDate, 120m));
        gate.Arm();
        var before = gate.PrepareCount;
        using var cancel = new CancellationTokenSource();
        await using var finalizerScope = host.Services.CreateAsyncScope();
        var running = finalizerScope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>()
            .FinalizeRegisterDirtyAsync(registerId, ct: cancel.Token);
        await gate.Prepared.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var blocker = host.Services.CreateAsyncScope();
        var uow = blocker.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.BeginTransactionAsync();
        await blocker.ServiceProvider.GetRequiredService<IAdvisoryLockManager>().LockOperationalRegisterAsync(registerId);
        gate.Release.TrySetResult();
        await gate.Completing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        await ((Func<Task>)(async () => await running.WaitAsync(TimeSpan.FromSeconds(5))))
            .Should().ThrowAsync<OperationCanceledException>();
        gate.PrepareCount.Should().Be(before + 1);
        (await ReadBalanceAsync(host, registerId)).Should().Be(100m);
        await uow.RollbackAsync();
        (await FinalizeDirtyAsync(host, registerId)).Should().Be(1);
    }

    [Fact]
    public async Task Unsafe_cached_sequence_is_rejected_before_publishing_a_projection()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (registerId, _) = await SeedProjectionAsync(host);
        await MarkMonthDirtyAsync(host, registerId, January);
        await using var scope = host.Services.CreateAsyncScope();
        var register = await scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>().GetByIdAsync(registerId);
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync();
        var sequence = await uow.Connection.ExecuteScalarAsync<string>(
            "SELECT pg_get_serial_sequence(@Table, 'movement_id')", new { Table = OperationalRegisterNaming.MovementsTable(register!.TableCode) });
        await uow.Connection.ExecuteAsync($"ALTER SEQUENCE {sequence} CACHE 8");
        try
        {
            await ((Func<Task>)(async () => await FinalizeDirtyAsync(host, registerId)))
                .Should().ThrowAsync<NgbConfigurationViolationException>();
            (await ReadBalanceAsync(host, registerId)).Should().Be(100m);
            (await scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>().GetAsync(registerId, January))!
                .Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);
        }
        finally { await uow.Connection.ExecuteAsync($"ALTER SEQUENCE {sequence} CACHE 1"); }
    }

    private static async Task AssertProjectionMatchesLedgerAsync(IHost host, Guid registerId, DateOnly month)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var register = await scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>().GetByIdAsync(registerId);
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync();
        var table = OperationalRegisterNaming.MovementsTable(register!.TableCode);
        // Independent oracle: sign each ledger entry and add it in C#, not the projection SQL.
        var rows = await uow.Connection.QueryAsync<LedgerAmount>($"SELECT amount AS Amount, is_storno AS Storno FROM {table} WHERE period_month <= @Month", new { Month = month });
        (await ReadBalanceAsync(host, registerId, month)).Should().Be(rows.Sum(row => row.Storno ? -row.Amount : row.Amount));
    }

    private sealed record LedgerAmount(decimal Amount, bool Storno);
}
