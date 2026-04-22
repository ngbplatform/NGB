using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P1: Period lock must be monthly, not day-specific.
/// Contract: callers may pass any date within a month; the lock must normalize to month start (YYYY-MM-01),
/// and it must block concurrent transactions targeting the same month.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdvisoryLocks_PeriodLock_NormalizesAndBlocks_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task LockPeriod_MidMonth_Blocks_FirstOfMonth_InAnotherTransaction()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var gate = new Barrier(2);

        var t1 = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var locks = scope.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            // Intentionally NOT the first of month: implementation must normalize.
            await locks.LockPeriodAsync(new DateOnly(2026, 1, 15), CancellationToken.None);

            gate.SignalAndWait(); // signal that the lock is held

            // Keep the transaction open long enough to ensure the second transaction will block.
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
            var act = () => locks.LockPeriodAsync(new DateOnly(2026, 1, 1), cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>("period lock must block while held by another transaction");

            await uow.RollbackAsync(CancellationToken.None);
        });

        await Task.WhenAll(t1, t2);

        // After the first transaction commits, acquiring the lock must succeed.
        await using var scope3 = host.Services.CreateAsyncScope();
        var uow3 = scope3.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var locks3 = scope3.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();

        await uow3.BeginTransactionAsync(CancellationToken.None);

        using var cts3 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await locks3.Invoking(l => l.LockPeriodAsync(new DateOnly(2026, 1, 31), cts3.Token))
            .Should().NotThrowAsync("lock must normalize and allow any date within the month when not held");

        await uow3.CommitAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LockPeriod_IsReentrant_WithinSameTransaction()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var locks = scope.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();

        await uow.BeginTransactionAsync(CancellationToken.None);

        await locks.Invoking(l => l.LockPeriodAsync(new DateOnly(2026, 2, 2), CancellationToken.None))
            .Should().NotThrowAsync();

        await locks.Invoking(l => l.LockPeriodAsync(new DateOnly(2026, 2, 28), CancellationToken.None))
            .Should().NotThrowAsync("acquiring the same monthly lock multiple times must not deadlock");

        await uow.RollbackAsync(CancellationToken.None);
    }
}
