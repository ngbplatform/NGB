using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P0: Advisory locks are a critical platform primitive.
/// These tests pin down contract aspects (requires active transaction, re-entrancy,
/// and blocking semantics across transactions).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdvisoryLocks_Contracts_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string TxnRequired = "Advisory locks require an active transaction. Call BeginTransactionAsync() first.";

    [Fact]
    public async Task LockMethods_WithoutActiveTransaction_Throw()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var locks = scope.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();

        await locks.Invoking(l => l.LockPeriodAsync(new DateOnly(2026, 1, 1), CancellationToken.None))
            .Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);

        await locks.Invoking(l => l.LockDocumentAsync(Guid.CreateVersion7(), CancellationToken.None))
            .Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);

        await locks.Invoking(l => l.LockCatalogAsync(Guid.CreateVersion7(), CancellationToken.None))
            .Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task LockDocument_IsReentrant_WithinSameTransaction()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var locks = scope.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();

        var id = Guid.CreateVersion7();

        await uow.BeginTransactionAsync(CancellationToken.None);

        await locks.Invoking(l => l.LockDocumentAsync(id, CancellationToken.None))
            .Should().NotThrowAsync();

        await locks.Invoking(l => l.LockDocumentAsync(id, CancellationToken.None))
            .Should().NotThrowAsync("acquiring the same xact lock multiple times must not deadlock");

        await uow.RollbackAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LockCatalog_IsReentrant_WithinSameTransaction()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var locks = scope.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();

        var id = Guid.CreateVersion7();

        await uow.BeginTransactionAsync(CancellationToken.None);

        await locks.Invoking(l => l.LockCatalogAsync(id, CancellationToken.None))
            .Should().NotThrowAsync();

        await locks.Invoking(l => l.LockCatalogAsync(id, CancellationToken.None))
            .Should().NotThrowAsync("acquiring the same xact lock multiple times must not deadlock");

        await uow.RollbackAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LockDocument_InSecondTransaction_Blocks_UntilFirstTransactionEnds()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var id = Guid.CreateVersion7();
        var gate = new Barrier(2);

        var t1 = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var locks = scope.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            await locks.LockDocumentAsync(id, CancellationToken.None);

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
            var act = () => locks.LockDocumentAsync(id, cts.Token);
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
        await locks3.Invoking(l => l.LockDocumentAsync(id, cts3.Token))
            .Should().NotThrowAsync();

        await uow3.CommitAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LockCatalog_InSecondTransaction_Blocks_UntilFirstTransactionEnds()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var id = Guid.CreateVersion7();
        var gate = new Barrier(2);

        var t1 = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var locks = scope.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            await locks.LockCatalogAsync(id, CancellationToken.None);

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
            var act = () => locks.LockCatalogAsync(id, cts.Token);
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
        await locks3.Invoking(l => l.LockCatalogAsync(id, cts3.Token))
            .Should().NotThrowAsync();

        await uow3.CommitAsync(CancellationToken.None);
    }
}
