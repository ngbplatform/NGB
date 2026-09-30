using System.Data;
using System.Data.Common;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.Locks;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

public sealed partial class OperationalRegisterCumulativeBalances_DefaultProjector_P0Tests
{
    [Theory]
    [InlineData("INCREMENT BY 2")]
    [InlineData("INCREMENT BY -1")]
    [InlineData("CYCLE")]
    public async Task Unsafe_sequence_order_is_rejected_without_mutating_committed_projections(string alteration)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (id, document) = await SeedProjectionAsync(host);
        await ApplyRepostAsync(host, id, document, CreateMovement(document, JanuaryDate, 120m));
        var versions = await ProjectionVersionsAsync(host, id);
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync();
        var register = await scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>().GetByIdAsync(id);
        var sequence = await uow.Connection.ExecuteScalarAsync<string>("SELECT pg_get_serial_sequence(@Table, 'movement_id')",
            new { Table = OperationalRegisterNaming.MovementsTable(register!.TableCode) });
        await uow.Connection.ExecuteAsync($"ALTER SEQUENCE {sequence} {alteration}");
        try
        {
            await ((Func<Task>)(async () => await FinalizeDirtyAsync(host, id))).Should().ThrowAsync<NgbConfigurationViolationException>();
            await AssertPublishedPairAsync(host, id, 100m, finalized: false);
            (await ProjectionVersionsAsync(host, id)).Should().Equal(versions);
        }
        finally { await uow.Connection.ExecuteAsync($"ALTER SEQUENCE {sequence} INCREMENT BY 1 NO CYCLE"); }
        (await FinalizeDirtyAsync(host, id)).Should().Be(1);
        await AssertPublishedPairAsync(host, id, 120m, finalized: true);
    }

    [Theory]
    [InlineData(IsolationLevel.RepeatableRead)]
    [InlineData(IsolationLevel.Serializable)]
    public async Task Stronger_isolation_uses_serialized_runner_and_refuses_direct_concurrent_preparation(IsolationLevel isolation)
    {
        var gate = new PreparationGate();
        using var host = GatedHost(gate, services => services.AddScoped<IUnitOfWork>(_ => new IsolationUow(Fixture.ConnectionString, isolation)));
        var (id, document) = await SeedProjectionAsync(host);
        await ApplyRepostAsync(host, id, document, CreateMovement(document, JanuaryDate, 120m));
        (await FinalizeDirtyAsync(host, id)).Should().Be(1);
        gate.PrepareCount.Should().Be(0, "the runner must retain serialized semantics outside ReadCommitted");
        await AssertPublishedPairAsync(host, id, 120m, finalized: true);
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.BeginTransactionAsync();
        var preparation = (IOperationalRegisterProjectionPreparation)scope.ServiceProvider.GetRequiredService<IOperationalRegisterDefaultProjectionRebuilder>();
        await ((Func<Task>)(async () => await preparation.PrepareMonthAsync(id, January)))
            .Should().ThrowAsync<NgbInvariantViolationException>();
        await uow.RollbackAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task External_transaction_keeps_publication_and_commit_under_caller_control(bool commit)
    {
        var gate = new PreparationGate();
        using var host = GatedHost(gate);
        var (id, document) = await SeedProjectionAsync(host);
        await ApplyRepostAsync(host, id, document, CreateMovement(document, JanuaryDate, 120m));
        var prepares = gate.PrepareCount;
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.BeginTransactionAsync();
        (await scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>()
            .FinalizeRegisterDirtyAsync(id, manageTransaction: false)).Should().Be(1);
        uow.HasActiveTransaction.Should().BeTrue();
        gate.PrepareCount.Should().Be(prepares);
        (await scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>().GetByMonthAsync(id, January))
            .Single().Values["amount"].Should().Be(120m);
        await AssertPublishedPairAsync(host, id, 100m, finalized: false);
        if (commit) await uow.CommitAsync(); else await uow.RollbackAsync();
        await AssertPublishedPairAsync(host, id, commit ? 120m : 100m, commit);
        (await FinalizeDirtyAsync(host, id)).Should().Be(commit ? 0 : 1);
        await AssertPublishedPairAsync(host, id, 120m, finalized: true);
    }

    [Fact]
    public async Task Boundary_capture_timeout_rolls_back_savepoint_and_exhausts_three_attempts_without_false_success()
    {
        var gate = new PreparationGate();
        using var host = GatedHost(gate, services => services.Configure<PostgresOptions>(o => o.OperationalRegisterPublicationTimeoutSeconds = 1));
        var (id, document) = await SeedProjectionAsync(host);
        await ApplyRepostAsync(host, id, document, CreateMovement(document, JanuaryDate, 120m));
        await using var blocker = host.Services.CreateAsyncScope();
        var blockerUow = blocker.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await blockerUow.BeginTransactionAsync();
        await blocker.ServiceProvider.GetRequiredService<IAdvisoryLockManager>().LockOperationalRegisterAsync(id);
        var attempts = gate.PrepareCount;
        try
        {
            await ((Func<Task>)(async () => await FinalizeDirtyAsync(host, id).WaitAsync(TimeSpan.FromSeconds(10))))
                .Should().ThrowAsync<OperationalRegisterFinalizationBusyException>();
            gate.PrepareCount.Should().Be(attempts + 3);
            await AssertPublishedPairAsync(host, id, 100m, finalized: false);
        }
        finally { await blockerUow.RollbackAsync(); }
        (await FinalizeDirtyAsync(host, id)).Should().Be(1);
        await AssertPublishedPairAsync(host, id, 120m, finalized: true);
    }

    [Fact]
    public async Task Cancelling_waiter_for_finalizer_lock_does_not_interrupt_the_owner_or_movement_writes()
    {
        var gate = new PreparationGate();
        using var host = GatedHost(gate);
        var (id, document) = await SeedProjectionAsync(host);
        await ApplyRepostAsync(host, id, document, CreateMovement(document, JanuaryDate, 120m));
        gate.Arm();
        var owner = FinalizeDirtyAsync(host, id);
        await gate.Prepared.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            using var cancel = new CancellationTokenSource();
            await using var scope = host.Services.CreateAsyncScope();
            var waiting = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>()
                .FinalizeRegisterDirtyAsync(id, ct: cancel.Token);
            var waitingUow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            using var waitDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!waitingUow.HasActiveTransaction) await Task.Delay(1, waitDeadline.Token);
            var backend = ((NpgsqlConnection)waitingUow.Connection).ProcessID;
            await using var observer = host.Services.CreateAsyncScope();
            var observerUow = observer.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await observerUow.EnsureConnectionOpenAsync();
            while (!await observerUow.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                       "SELECT cardinality(pg_blocking_pids(@Backend)) > 0", new { Backend = backend }, cancellationToken: waitDeadline.Token)))
                await Task.Delay(1, waitDeadline.Token);
            cancel.Cancel();
            await ((Func<Task>)(async () => await waiting.WaitAsync(TimeSpan.FromSeconds(5))))
                .Should().ThrowAsync<OperationCanceledException>();
            owner.IsCompleted.Should().BeFalse();
            await AppendFaultTailAsync(host, id).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { gate.Release.TrySetResult(); }
        (await owner.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(1);
        await AssertPublishedPairAsync(host, id, 150m, finalized: true);
    }

    private sealed class IsolationUow(string connectionString, IsolationLevel isolation) : IUnitOfWork
    {
        public DbConnection Connection { get; } = new NpgsqlConnection(connectionString);
        public DbTransaction? Transaction { get; private set; }
        public bool HasActiveTransaction => Transaction is not null;
        public async Task EnsureConnectionOpenAsync(CancellationToken ct = default)
        { if (Connection.State != ConnectionState.Open) await Connection.OpenAsync(ct); }
        public async Task BeginTransactionAsync(CancellationToken ct = default)
        { await EnsureConnectionOpenAsync(ct); Transaction ??= await Connection.BeginTransactionAsync(isolation, ct); }
        public async Task CommitAsync(CancellationToken ct = default)
        { await Transaction!.CommitAsync(CancellationToken.None); await Transaction.DisposeAsync(); Transaction = null; }
        public async Task RollbackAsync(CancellationToken ct = default)
        { if (Transaction is null) return; await Transaction.RollbackAsync(CancellationToken.None); await Transaction.DisposeAsync(); Transaction = null; }
        public void EnsureActiveTransaction() { if (Transaction is null) throw new NgbInvariantViolationException("Test transaction required."); }
        public async ValueTask DisposeAsync() { await RollbackAsync(); await Connection.DisposeAsync(); }
    }
}
