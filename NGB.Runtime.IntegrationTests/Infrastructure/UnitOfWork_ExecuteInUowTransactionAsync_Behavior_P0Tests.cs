using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P0: contract test for IUnitOfWork.ExecuteInUowTransactionAsync:
/// - when there is no ambient transaction, it must commit work,
/// - when there is an ambient transaction, it must not commit/rollback implicitly (outer transaction controls the outcome).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UnitOfWork_ExecuteInUowTransactionAsync_Behavior_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime NowUtc = new(2026, 2, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ExecuteInUowTransactionAsync_WhenNoAmbientTransaction_CommitsWork()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var documentId = Guid.CreateVersion7();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

            await uow.ExecuteInUowTransactionAsync(async ct =>
            {
                await docs.CreateAsync(new DocumentRecord
                {
                    Id = documentId,
                    TypeCode = "it_doc_uow",
                    Number = "UOW-1",
                    DateUtc = NowUtc,
                    Status = DocumentStatus.Draft,
                    CreatedAtUtc = NowUtc,
                    UpdatedAtUtc = NowUtc,
                    PostedAtUtc = null,
                    MarkedForDeletionAtUtc = null
                }, ct);
            }, CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var doc = await docs.GetAsync(documentId, CancellationToken.None);

            doc.Should().NotBeNull();
            doc!.Number.Should().Be("UOW-1");
        }
    }

    [Fact]
    public async Task ExecuteInUowTransactionAsync_WhenAmbientTransactionExists_DoesNotCommit_OuterRollbackRemovesWork()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var documentId = Guid.CreateVersion7();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            try
            {
                await uow.ExecuteInUowTransactionAsync(manageTransaction: false, async ct =>
                {
                    await docs.CreateAsync(new DocumentRecord
                    {
                        Id = documentId,
                        TypeCode = "it_doc_uow",
                        Number = "UOW-ROLLBACK",
                        DateUtc = NowUtc,
                        Status = DocumentStatus.Draft,
                        CreatedAtUtc = NowUtc,
                        UpdatedAtUtc = NowUtc,
                        PostedAtUtc = null,
                        MarkedForDeletionAtUtc = null
                    }, ct);
                }, CancellationToken.None);

                // The nested helper must not have committed the ambient transaction.
                uow.HasActiveTransaction.Should().BeTrue();

                await uow.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                try { await uow.RollbackAsync(CancellationToken.None); } catch { /* ignore */ }
                throw;
            }
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var doc = await docs.GetAsync(documentId, CancellationToken.None);

            doc.Should().BeNull("outer rollback must undo work performed inside ExecuteInUowTransactionAsync");
        }
    }
}
