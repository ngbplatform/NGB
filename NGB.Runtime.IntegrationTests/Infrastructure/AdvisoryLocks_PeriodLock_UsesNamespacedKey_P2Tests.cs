using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Locks;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P2: Period advisory locks must use the two-int namespaced key form:
///   pg_advisory_xact_lock(key1, key2)
///
/// This pins down the key layout:
///   key1 = 'PER\x01'
///   key2 = YYYYMM (e.g., 202601)
///
/// Additionally, LockPeriodAsync is monthly and must normalize any date to YYYY-MM-01.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdvisoryLocks_PeriodLock_UsesNamespacedKey_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task LockPeriodAsync_UsesNamespacedKey_And_NormalizesToMonthStart()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var locks = scope.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();

        await uow.BeginTransactionAsync(CancellationToken.None);

        // Pass a non-month-start date on purpose; implementation must normalize to YYYY-MM-01.
        var dateInsideMonth = new DateOnly(2026, 1, 15);
        await locks.LockPeriodAsync(dateInsideMonth, CancellationToken.None);

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

        var expectedKey1 = AdvisoryLockNamespaces.Period;
        const int expectedKey2 = 202601;

        rows.Should().Contain(r =>
            r.ClassId == expectedKey1 &&
            r.ObjId == expectedKey2 &&
            r.Mode == "ExclusiveLock" &&
            r.Granted);

        await uow.RollbackAsync(CancellationToken.None);
    }

    private sealed record LockRow(int ClassId, int ObjId, string Mode, bool Granted);
}
