using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

/// <summary>
/// P0: IDocumentRelationshipService must fully support external transaction mode:
/// - manageTransaction=false without an ambient transaction => fail fast with canonical message,
/// - manageTransaction=false with an ambient transaction => use it and must not commit/rollback implicitly.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationshipService_ExternalTransactionMode_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime NowUtc = new(2026, 2, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CreateAsync_WhenManageTransactionFalse_AndNoAmbientTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();
        await SeedDraftDocumentsAsync(host, fromId, toId);

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        var act = () => svc.CreateAsync(
            fromDocumentId: fromId,
            toDocumentId: toId,
            relationshipCode: "based_on",
            manageTransaction: false,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("This operation requires an active transaction.");
    }

    [Fact]
    public async Task DeleteAsync_WhenManageTransactionFalse_AndNoAmbientTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();
        await SeedDraftDocumentsAsync(host, fromId, toId);

        // Create relationship in normal mode so that Delete path is meaningful.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            await svc.CreateAsync(fromId, toId, "based_on", manageTransaction: true, ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

            var act = () => svc.DeleteAsync(
                fromDocumentId: fromId,
                toDocumentId: toId,
                relationshipCode: "based_on",
                manageTransaction: false,
                ct: CancellationToken.None);

            await act.Should().ThrowAsync<NgbInvariantViolationException>()
                .WithMessage("This operation requires an active transaction.");
        }
    }

    [Fact]
    public async Task CreateAsync_WhenManageTransactionFalse_UsesAmbientTransaction_AndDoesNotCommit()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();
        await SeedDraftDocumentsAsync(host, fromId, toId);

        var relationshipId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|based_on|{toId:D}");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            try
            {
                await svc.CreateAsync(fromId, toId, "based_on", manageTransaction: false, ct: CancellationToken.None);

                uow.HasActiveTransaction.Should().BeTrue("external transaction mode must not auto-commit");

                await uow.CommitAsync(CancellationToken.None);
            }
            catch
            {
                try { await uow.RollbackAsync(CancellationToken.None); } catch { /* ignore */ }
                throw;
            }
        }

        (await CountRelationshipRowsAsync(relationshipId)).Should().Be(1);
    }

    [Fact]
    public async Task DeleteAsync_WhenManageTransactionFalse_UsesAmbientTransaction_AndDoesNotCommit()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();
        await SeedDraftDocumentsAsync(host, fromId, toId);

        var relationshipId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|based_on|{toId:D}");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            await svc.CreateAsync(fromId, toId, "based_on", manageTransaction: true, ct: CancellationToken.None);
        }

        (await CountRelationshipRowsAsync(relationshipId)).Should().Be(1);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            try
            {
                await svc.DeleteAsync(fromId, toId, "based_on", manageTransaction: false, ct: CancellationToken.None);

                uow.HasActiveTransaction.Should().BeTrue("external transaction mode must not auto-commit");

                await uow.CommitAsync(CancellationToken.None);
            }
            catch
            {
                try { await uow.RollbackAsync(CancellationToken.None); } catch { /* ignore */ }
                throw;
            }
        }

        (await CountRelationshipRowsAsync(relationshipId)).Should().Be(0);
    }

    private static async Task SeedDraftDocumentsAsync(IHost host, Guid fromId, Guid toId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await repo.CreateAsync(new DocumentRecord
            {
                Id = fromId,
                TypeCode = "it.doc.from",
                Number = "FROM-EXTTX",
                DateUtc = NowUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = NowUtc,
                UpdatedAtUtc = NowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);

            await repo.CreateAsync(new DocumentRecord
            {
                Id = toId,
                TypeCode = "it.doc.to",
                Number = "TO-EXTTX",
                DateUtc = NowUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = NowUtc,
                UpdatedAtUtc = NowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);
    }

    private async Task<int> CountRelationshipRowsAsync(Guid relationshipId)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM document_relationships WHERE relationship_id = @id;",
            new { id = relationshipId });
    }
}
