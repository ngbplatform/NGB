using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Locks;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Locks;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P2: Period advisory locks must be scoped by subsystem.
///
/// Accounting uses 'PER\x01' while Operational Registers use 'ORP\x01'.
/// This prevents unnecessary cross-subsystem serialization when both legitimately
/// operate on the same calendar month.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdvisoryLocks_PeriodLock_ScopeIsolation_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task LockPeriodAsync_WhenScopeIsOperationalRegister_UsesOpRegNamespaceKey_And_NormalizesToMonthStart()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var locks = scope.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();

        await uow.BeginTransactionAsync(CancellationToken.None);

        // Pass a non-month-start date on purpose; implementation must normalize to YYYY-MM-01.
        var dateInsideMonth = new DateOnly(2026, 1, 31);
        await locks.LockPeriodAsync(dateInsideMonth, AdvisoryLockPeriodScope.OperationalRegister, CancellationToken.None);

        await uow.EnsureConnectionOpenAsync(CancellationToken.None);
        uow.Transaction.Should().NotBeNull("transaction must be active for xact locks");

        var pid = await uow.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "select pg_backend_pid();",
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

        var rows = (await uow.Connection.QueryAsync<LockRow>(
            new CommandDefinition(
                """
                select classid::int as ClassId, objid::int as ObjId, mode as Mode, granted as Granted
                from pg_locks
                where locktype = 'advisory'
                  and pid = @Pid;
                """,
                new { Pid = pid },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None))).ToList();

        var expectedKey1 = AdvisoryLockNamespaces.OperationalRegisterPeriod;
        const int expectedKey2 = 202601;

        rows.Should().Contain(r =>
            r.ClassId == expectedKey1 &&
            r.ObjId == expectedKey2 &&
            r.Mode == "ExclusiveLock" &&
            r.Granted);

        await uow.RollbackAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PeriodLocks_ForSameMonth_DoNotConflict_BetweenAccountingAndOperationalRegisterScopes()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Txn #1 (Accounting): hold PER lock for the month.
        await using var scope1 = host.Services.CreateAsyncScope();
        var uow1 = scope1.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var locks1 = scope1.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();

        await uow1.BeginTransactionAsync(CancellationToken.None);
        await locks1.LockPeriodAsync(new DateOnly(2026, 1, 15), CancellationToken.None); // default = Accounting
        await uow1.EnsureConnectionOpenAsync(CancellationToken.None);

        // Txn #2: attempt to acquire locks for the same month.
        await using var scope2 = host.Services.CreateAsyncScope();
        var uow2 = scope2.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow2.BeginTransactionAsync(CancellationToken.None);
        await uow2.EnsureConnectionOpenAsync(CancellationToken.None);

        const int key2 = 202601;

        // Different scope must NOT conflict with PER.
        var gotOpReg = await uow2.Connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                "select pg_try_advisory_xact_lock(@Key1, @Key2);",
                new { Key1 = AdvisoryLockNamespaces.OperationalRegisterPeriod, Key2 = key2 },
                transaction: uow2.Transaction,
                cancellationToken: CancellationToken.None));

        gotOpReg.Should().BeTrue("operational register period lock must not conflict with accounting period lock");

        // Same scope MUST conflict.
        var gotAccounting = await uow2.Connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                "select pg_try_advisory_xact_lock(@Key1, @Key2);",
                new { Key1 = AdvisoryLockNamespaces.Period, Key2 = key2 },
                transaction: uow2.Transaction,
                cancellationToken: CancellationToken.None));

        gotAccounting.Should().BeFalse("accounting period lock should already be held by the first transaction");

        await uow2.RollbackAsync(CancellationToken.None);
        await uow1.RollbackAsync(CancellationToken.None);
    }

    private sealed record LockRow(int ClassId, int ObjId, string Mode, bool Granted);
}
