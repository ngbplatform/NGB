using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Locks;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PlatformPostgresCollection.Name)]
public sealed class AdvisoryLocks_OperationalRegisterPublication_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PublicationLocks_ProtectRegisterAndMonth_AndReleaseAtTransactionEnd(bool sharedMonth, bool commit)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var publisherScope = host.Services.CreateAsyncScope();
        await using var competitorScope = host.Services.CreateAsyncScope();
        var publisher = publisherScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var competitor = competitorScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var locks = (IOperationalRegisterSharedPeriodLockManager)publisherScope.ServiceProvider
            .GetRequiredService<IAdvisoryLockManager>();
        var registerId = Guid.CreateVersion7();
        var month = new DateOnly(2026, 8, 15);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await publisher.BeginTransactionAsync(deadline.Token);
        if (sharedMonth)
            await locks.LockOperationalRegisterSharedPublicationAsync(registerId, month, deadline.Token);
        else
            await locks.LockOperationalRegisterPublicationAsync(registerId, month, deadline.Token);

        var held = (await publisher.Connection.QueryAsync<LockRow>(new CommandDefinition(
            """
            SELECT classid::int AS ClassId, objid::int AS ObjId, mode AS Mode
            FROM pg_locks
            WHERE locktype = 'advisory' AND pid = pg_backend_pid() AND granted;
            """,
            transaction: publisher.Transaction,
            cancellationToken: deadline.Token))).ToList();

        held.Should().HaveCount(3);
        var registerLocks = held.Where(row => row.ClassId == AdvisoryLockNamespaces.OperationalRegister).ToList();
        registerLocks.Should().HaveCount(2).And.OnlyContain(row => row.Mode == "ExclusiveLock");
        held.Should().ContainSingle(row => row.ClassId == AdvisoryLockNamespaces.OperationalRegisterPeriod)
            .Which.Should().Be(new LockRow(
                AdvisoryLockNamespaces.OperationalRegisterPeriod, 202608, sharedMonth ? "ShareLock" : "ExclusiveLock"));

        await competitor.BeginTransactionAsync(deadline.Token);
        foreach (var row in registerLocks)
            (await TryLockAsync(competitor, row.ClassId, row.ObjId, shared: false, deadline.Token)).Should().BeFalse();

        (await TryLockAsync(competitor, AdvisoryLockNamespaces.OperationalRegisterPeriod, 202608, shared: true, deadline.Token))
            .Should().Be(sharedMonth, "only shared publication permits concurrent shared month locks");
        (await TryLockAsync(competitor, AdvisoryLockNamespaces.OperationalRegisterPeriod, 202608, shared: false, deadline.Token))
            .Should().BeFalse("even shared publication must exclude an exclusive month guard");
        (await TryLockAsync(competitor, AdvisoryLockNamespaces.OperationalRegisterPeriod, 202609, shared: false, deadline.Token))
            .Should().BeTrue("another month must remain available");

        if (commit)
            await publisher.CommitAsync(deadline.Token);
        else
            await publisher.RollbackAsync(deadline.Token);

        foreach (var row in held)
            (await TryLockAsync(competitor, row.ClassId, row.ObjId, shared: false, deadline.Token)).Should().BeTrue();

        await competitor.RollbackAsync(deadline.Token);
    }

    private static Task<bool> TryLockAsync(IUnitOfWork uow, int key1, int key2, bool shared, CancellationToken ct)
    {
        var sql = shared
            ? "SELECT pg_try_advisory_xact_lock_shared(@Key1, @Key2);"
            : "SELECT pg_try_advisory_xact_lock(@Key1, @Key2);";

        return uow.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            sql,
            new { Key1 = key1, Key2 = key2 },
            transaction: uow.Transaction,
            cancellationToken: ct));
    }

    private sealed record LockRow(int ClassId, int ObjId, string Mode);
}
