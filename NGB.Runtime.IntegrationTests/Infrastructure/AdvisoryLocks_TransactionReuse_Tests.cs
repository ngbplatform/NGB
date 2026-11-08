using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PlatformPostgresCollection.Name)]
public sealed class AdvisoryLocks_TransactionReuse_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DocumentLock_AfterCommitOrRollbackInSameScope_BlocksAnotherTransaction(
        bool rollbackFirst,
        bool useBatch)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var owner = host.Services.CreateAsyncScope();
        var uow = owner.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var locks = owner.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();
        var documentId = Guid.CreateVersion7();

        Task AcquireOwnerLockAsync()
            => useBatch
                ? ((IAdvisoryLockBatchManager)locks).LockDocumentsAsync([documentId])
                : locks.LockDocumentAsync(documentId);

        async Task AssertContenderBlockedAsync()
        {
            await using var contender = host.Services.CreateAsyncScope();
            var contenderUow = contender.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await contenderUow.BeginTransactionAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
            Func<Task> acquire = () => contender.ServiceProvider.GetRequiredService<IAdvisoryLockManager>()
                .LockDocumentAsync(documentId, timeout.Token);
            try
            {
                await acquire.Should().ThrowAsync<OperationCanceledException>(
                    "the owner must hold the document lock in each transaction");
            }
            finally
            {
                await contenderUow.RollbackAsync();
            }
        }

        await uow.BeginTransactionAsync();
        await AcquireOwnerLockAsync();
        await AssertContenderBlockedAsync();
        if (rollbackFirst)
            await uow.RollbackAsync();
        else
            await uow.CommitAsync();

        await uow.BeginTransactionAsync();
        try
        {
            await AcquireOwnerLockAsync();
            await locks.LockDocumentAsync(documentId);
            await AssertContenderBlockedAsync();
        }
        finally
        {
            await uow.RollbackAsync();
        }

        await using var after = host.Services.CreateAsyncScope();
        var afterUow = after.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await afterUow.BeginTransactionAsync();
        using var releasedTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await after.ServiceProvider.GetRequiredService<IAdvisoryLockManager>()
            .LockDocumentAsync(documentId, releasedTimeout.Token);
        await afterUow.RollbackAsync();
    }
}
