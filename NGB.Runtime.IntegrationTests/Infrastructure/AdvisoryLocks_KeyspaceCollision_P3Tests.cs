using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P3: Advisory lock keys for different aggregates (Document vs Catalog) MUST NOT collide.
///
/// Earlier versions derived keys only from Guid bytes, so the same Guid could block across
/// aggregates. The lock manager now namespaces keys by aggregate type.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdvisoryLocks_KeyspaceIsolation_P3Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task LockDocument_And_LockCatalog_WithSameGuid_DoNotBlockEachOther()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Same Guid used for different aggregates.
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var lockAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var holder = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var locks = scope.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            await locks.LockDocumentAsync(id, CancellationToken.None);

            lockAcquired.SetResult();
            await releaseLock.Task;

            await uow.CommitAsync(CancellationToken.None);
        });

        await lockAcquired.Task;

        var shouldNotBlock = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var locks = scope.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            // Must succeed even while the document lock is held.
            await locks.Invoking(l => l.LockCatalogAsync(id, cts.Token))
                .Should().NotThrowAsync("document and catalog locks must be in different key namespaces");

            await uow.CommitAsync(CancellationToken.None);
        });

        await shouldNotBlock;
        releaseLock.SetResult();
        await holder;

        // After the document lock is released, catalog lock must succeed.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var locks = scope.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            await locks.LockCatalogAsync(id, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }
    }
}
