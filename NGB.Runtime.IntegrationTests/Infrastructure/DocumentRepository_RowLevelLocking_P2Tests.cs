using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P2: Row-level locking for documents is a platform invariant. These tests ensure
/// SELECT ... FOR UPDATE really serializes concurrent state transitions.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentRepository_RowLevelLocking_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetForUpdate_BlocksConcurrentUpdateStatus_UntilTransactionCompletes()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var docId = Guid.CreateVersion7();
        var t0 = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        await CreateDraftDocumentAsync(host, docId, t0);

        var lockAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var holder = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            var row = await repo.GetForUpdateAsync(docId, CancellationToken.None);
            row.Should().NotBeNull();

            lockAcquired.SetResult();
            await releaseLock.Task;

            await uow.CommitAsync(CancellationToken.None);
        });

        await lockAcquired.Task;

        var blockedUpdate = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            var act = () => repo.UpdateStatusAsync(
                documentId: docId,
                status: DocumentStatus.Posted,
                updatedAtUtc: t0.AddMinutes(1),
                postedAtUtc: t0.AddMinutes(1),
                markedForDeletionAtUtc: null,
                ct: cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>("row lock must block concurrent update");
            await uow.RollbackAsync(CancellationToken.None);
        });

        await blockedUpdate;
        releaseLock.SetResult();
        await holder;

        // Now the update must succeed.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            await repo.UpdateStatusAsync(
                documentId: docId,
                status: DocumentStatus.Posted,
                updatedAtUtc: t0.AddMinutes(2),
                postedAtUtc: t0.AddMinutes(2),
                markedForDeletionAtUtc: null,
                ct: CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var doc = await repo.GetAsync(docId, CancellationToken.None);

            doc.Should().NotBeNull();
            doc!.Status.Should().Be(DocumentStatus.Posted);
        }
    }

    [Fact]
    public async Task UpdateStatus_WhenDocumentDoesNotExist_ThrowsAndDoesNotWrite()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var missingId = Guid.CreateVersion7();

        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        await uow.BeginTransactionAsync(CancellationToken.None);

        var act = () => repo.UpdateStatusAsync(
            documentId: missingId,
            status: DocumentStatus.Posted,
            updatedAtUtc: DateTime.UtcNow,
            postedAtUtc: DateTime.UtcNow,
            markedForDeletionAtUtc: null,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DocumentNotFoundException>();
        ex.Which.DocumentId.Should().Be(missingId);

        await uow.RollbackAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Create_WhenDateUtcIsNotUtc_Throws_AndDoesNotInsert()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var docId = Guid.CreateVersion7();
        var nowUtc = DateTime.UtcNow;
        var localDate = DateTime.SpecifyKind(nowUtc, DateTimeKind.Local);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            var act = () => repo.CreateAsync(new DocumentRecord
            {
                Id = docId,
                TypeCode = "IT",
                Number = "0001",
                DateUtc = localDate,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, CancellationToken.None);

            var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
            ex.Which.ParamName.Should().Be("DateUtc");
            ex.Which.Message.Should().Contain("must be UTC");

            await uow.RollbackAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var doc = await repo.GetAsync(docId, CancellationToken.None);
            doc.Should().BeNull();
        }
    }

    private static async Task CreateDraftDocumentAsync(IHost host, Guid docId, DateTime nowUtc)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        await uow.BeginTransactionAsync(CancellationToken.None);
        await repo.CreateAsync(new DocumentRecord
        {
            Id = docId,
            TypeCode = "IT",
            Number = "0001",
            DateUtc = nowUtc,
            Status = DocumentStatus.Draft,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null
        }, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);
    }
}
