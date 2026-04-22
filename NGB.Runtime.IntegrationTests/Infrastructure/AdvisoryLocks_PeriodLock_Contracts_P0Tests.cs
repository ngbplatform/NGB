using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P0: Period advisory locks are a critical platform primitive.
/// These tests pin down contract aspects (re-entrancy and blocking semantics across transactions).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdvisoryLocks_PeriodLock_Contracts_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task LockPeriod_IsReentrant_WithinSameTransaction()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var locks = scope.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();

        // Any date in the month must map to the same month lock.
        var period = new DateOnly(2026, 1, 15);

        await uow.BeginTransactionAsync(CancellationToken.None);

        await locks.Invoking(l => l.LockPeriodAsync(period, CancellationToken.None))
            .Should().NotThrowAsync();

        await locks.Invoking(l => l.LockPeriodAsync(period, CancellationToken.None))
            .Should().NotThrowAsync("acquiring the same xact lock multiple times must not deadlock");

        await uow.RollbackAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LockPeriod_InSecondTransaction_Blocks_UntilFirstTransactionEnds()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var period = new DateOnly(2026, 2, 10);
        var gate = new Barrier(2);

        var t1 = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var locks = scope.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            await locks.LockPeriodAsync(period, CancellationToken.None);

            gate.SignalAndWait(); // signal that the lock is held

            // keep transaction open long enough to ensure the second transaction will block
            await Task.Delay(TimeSpan.FromSeconds(2));
            await uow.CommitAsync(CancellationToken.None);
        });

        var t2 = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var locks = scope.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            gate.SignalAndWait();

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            var act = () => locks.LockPeriodAsync(period, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>("lock must block while held by another transaction");

            await uow.RollbackAsync(CancellationToken.None);
        });

        await Task.WhenAll(t1, t2);

        // After t1 commits, acquiring the lock must succeed.
        await using var scope3 = host.Services.CreateAsyncScope();
        var uow3 = scope3.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var locks3 = scope3.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();
        await uow3.BeginTransactionAsync(CancellationToken.None);

        using var cts3 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await locks3.Invoking(l => l.LockPeriodAsync(period, cts3.Token))
            .Should().NotThrowAsync();

        await uow3.CommitAsync(CancellationToken.None);
    }
}
