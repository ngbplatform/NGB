using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Catalogs;
using NGB.Core.Catalogs.Exceptions;
using NGB.Persistence.Catalogs;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P2: Row-level locking for catalogs is required to serialize deletions/updates.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CatalogRepository_RowLevelLocking_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetForUpdate_BlocksConcurrentMarkDeleted_UntilTransactionCompletes()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var catalogId = Guid.CreateVersion7();
        var t0 = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        await CreateCatalogAsync(host, catalogId, t0);

        var lockAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var holder = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            var row = await repo.GetForUpdateAsync(catalogId, CancellationToken.None);
            row.Should().NotBeNull();

            lockAcquired.SetResult();
            await releaseLock.Task;

            await uow.CommitAsync(CancellationToken.None);
        });

        await lockAcquired.Task;

        var blockedDelete = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            var act = () => repo.MarkForDeletionAsync(catalogId, updatedAtUtc: t0.AddMinutes(1), ct: cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>("row lock must block concurrent update");
            await uow.RollbackAsync(CancellationToken.None);
        });

        await blockedDelete;
        releaseLock.SetResult();
        await holder;

        // Now deletion must succeed.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            await repo.MarkForDeletionAsync(catalogId, updatedAtUtc: t0.AddMinutes(2), ct: CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();
            var cat = await repo.GetAsync(catalogId, CancellationToken.None);
            cat.Should().NotBeNull();
            cat!.IsDeleted.Should().BeTrue();
        }
    }

    [Fact]
    public async Task MarkDeleted_WhenCatalogDoesNotExist_ThrowsAndDoesNotWrite()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var missingId = Guid.CreateVersion7();

        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();

        await uow.BeginTransactionAsync(CancellationToken.None);

        var act = () => repo.MarkForDeletionAsync(missingId, updatedAtUtc: DateTime.UtcNow, ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<CatalogNotFoundException>();
        ex.Which.CatalogId.Should().Be(missingId);

        await uow.RollbackAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Create_WhenTimestampsAreNotUtc_Throws_AndDoesNotInsert()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var id = Guid.CreateVersion7();
        var nowUtc = DateTime.UtcNow;
        var local = DateTime.SpecifyKind(nowUtc, DateTimeKind.Local);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            var act = () => repo.CreateAsync(new CatalogRecord
            {
                Id = id,
                CatalogCode = "IT",
                IsDeleted = false,
                CreatedAtUtc = local,
                UpdatedAtUtc = nowUtc
            }, CancellationToken.None);

            var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
            ex.Which.ParamName.Should().Be("CreatedAtUtc");
            ex.Which.Message.Should().Contain("must be UTC");

            await uow.RollbackAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();
            var cat = await repo.GetAsync(id, CancellationToken.None);
            cat.Should().BeNull();
        }
    }

    private static async Task CreateCatalogAsync(IHost host, Guid id, DateTime nowUtc)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();

        await uow.BeginTransactionAsync(CancellationToken.None);

        await repo.CreateAsync(new CatalogRecord
        {
            Id = id,
            CatalogCode = "IT",
            IsDeleted = false,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        }, CancellationToken.None);

        await uow.CommitAsync(CancellationToken.None);
    }
}
