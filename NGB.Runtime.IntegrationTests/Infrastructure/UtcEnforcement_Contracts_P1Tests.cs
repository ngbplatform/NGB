using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Core.Documents;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Documents;
using NGB.Persistence.PostingState;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class UtcEnforcement_Contracts_P1Tests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task PostingLogRepository_TryBegin_WhenStartedAtIsNotUtc_Throws_AndDoesNotInsert()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var documentId = Guid.CreateVersion7();
        var startedAtLocal = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Local);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IPostingStateRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            var act = () => repo.TryBeginAsync(documentId, PostingOperation.Post, startedAtLocal, CancellationToken.None);

            var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
            ex.Which.ParamName.Should().Be("startedAtUtc");
            ex.Which.Message.Should().Contain("must be UTC");

            await uow.RollbackAsync(CancellationToken.None);
        }

        // Ensure no row inserted for the document.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();

            var page = await reader.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddHours(-2),
                ToUtc = DateTime.UtcNow.AddHours(2),
                DocumentId = documentId,
                Operation = PostingOperation.Post,
                PageSize = 20
            }, CancellationToken.None);

            page.Records.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task CatalogRepository_MarkDeleted_WhenUpdatedAtIsNotUtc_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var updatedAtLocal = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();

        await uow.BeginTransactionAsync(CancellationToken.None);

        var act = () => repo.MarkForDeletionAsync(Guid.CreateVersion7(), updatedAtLocal, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("updatedAtUtc");
        ex.Which.Message.Should().Contain("must be UTC");

        await uow.RollbackAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DocumentRepository_UpdateStatus_WhenUpdatedAtIsNotUtc_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var documentId = Guid.CreateVersion7();
        var nowUtc = DateTime.UtcNow;

        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        // Create a document inside a transaction.
        await uow.BeginTransactionAsync(CancellationToken.None);
        await repo.CreateAsync(new DocumentRecord
        {
            Id = documentId,
            TypeCode = "TEST",
            Number = "0001",
            DateUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            Status = DocumentStatus.Draft,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        }, CancellationToken.None);

        var updatedAtLocal = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Local);

        var act = () => repo.UpdateStatusAsync(
            documentId: documentId,
            status: DocumentStatus.Posted,
            postedAtUtc: nowUtc,
            markedForDeletionAtUtc: null,
            updatedAtUtc: updatedAtLocal,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("updatedAtUtc");
        ex.Which.Message.Should().Contain("must be UTC");

        await uow.RollbackAsync(CancellationToken.None);
    }
}
