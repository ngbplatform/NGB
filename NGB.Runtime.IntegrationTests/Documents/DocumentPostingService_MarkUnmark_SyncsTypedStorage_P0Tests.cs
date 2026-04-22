using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Definitions.Documents.Numbering;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Documents.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_MarkUnmark_SyncsTypedStorage_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string TypeCode = "it_doc_mark_sync";
    private const string TypedTable = "doc_it_doc_mark_sync";

    [Fact]
    public async Task MarkAndUnmarkForDeletion_WhenStorageImplementsFullHook_SynchronizesTypedStorageAndIsIdempotent()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItDocMarkSyncContributor>();
                services.AddSingleton<ItDocMarkSyncNumberingPolicy>();
                services.AddScoped<ItDocMarkSyncStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<ItDocMarkSyncStorage>());
            });

        var date = new DateTime(2026, 01, 10, 0, 0, 0, DateTimeKind.Utc);

        Guid id;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            id = await drafts.CreateDraftAsync(TypeCode, number: "M-1", dateUtc: date, manageTransaction: true, ct: CancellationToken.None);
        }

        // Act 1: Mark for deletion (should sync typed storage)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.MarkForDeletionAsync(id, CancellationToken.None);

            // Act 1b: Idempotent no-op
            await posting.MarkForDeletionAsync(id, CancellationToken.None);
        }

        // Assert after mark
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var row = await conn.QuerySingleAsync<(int UpdateCalls, string? LastStatus, DateTime? LastMarkedAt)>(
                $"SELECT update_calls AS UpdateCalls, last_status AS LastStatus, last_marked_for_deletion_at_utc AS LastMarkedAt FROM {TypedTable} WHERE document_id = @id;",
                new { id });

            row.UpdateCalls.Should().Be(1);
            row.LastStatus.Should().Be(DocumentStatus.MarkedForDeletion.ToString());
            row.LastMarkedAt.Should().NotBeNull();
        }

        // Act 2: Unmark for deletion (should sync typed storage)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.UnmarkForDeletionAsync(id, CancellationToken.None);

            // Act 2b: Idempotent no-op
            await posting.UnmarkForDeletionAsync(id, CancellationToken.None);
        }

        // Assert after unmark
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var row = await conn.QuerySingleAsync<(int UpdateCalls, string? LastStatus, DateTime? LastMarkedAt)>(
                $"SELECT update_calls AS UpdateCalls, last_status AS LastStatus, last_marked_for_deletion_at_utc AS LastMarkedAt FROM {TypedTable} WHERE document_id = @id;",
                new { id });

            row.UpdateCalls.Should().Be(2);
            row.LastStatus.Should().Be(DocumentStatus.Draft.ToString());
            row.LastMarkedAt.Should().BeNull();
        }
    }

    private static async Task EnsureTypedTableExistsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var ddl = $"""
CREATE TABLE IF NOT EXISTS {TypedTable} (
    document_id UUID PRIMARY KEY REFERENCES documents(id) ON DELETE RESTRICT,
    update_calls INT NOT NULL DEFAULT 0,
    last_status TEXT NULL,
    last_marked_for_deletion_at_utc TIMESTAMPTZ NULL
);
""";

        await conn.ExecuteAsync(ddl);
    }

    private sealed class ItDocMarkSyncContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(TypeCode, b => b
                .Metadata(new DocumentTypeMetadata(
                    TypeCode,
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Doc Mark Sync"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .TypedStorage<ItDocMarkSyncStorage>()
                .NumberingPolicy<ItDocMarkSyncNumberingPolicy>());
        }
    }

    private sealed class ItDocMarkSyncNumberingPolicy : IDocumentNumberingPolicy
    {
        public string TypeCode => DocumentPostingService_MarkUnmark_SyncsTypedStorage_P0Tests.TypeCode;
        public bool EnsureNumberOnCreateDraft => false;
        public bool EnsureNumberOnPost => false;
    }

    private sealed class ItDocMarkSyncStorage(IUnitOfWork uow)
        : IDocumentTypeStorage, IDocumentTypeDraftFullUpdater
    {
        public string TypeCode => DocumentPostingService_MarkUnmark_SyncsTypedStorage_P0Tests.TypeCode;

        public async Task CreateDraftAsync(Guid documentId, CancellationToken ct = default)
        {
            uow.EnsureActiveTransaction();

            var sql = $"INSERT INTO {TypedTable} (document_id) VALUES (@documentId) ON CONFLICT (document_id) DO NOTHING;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(sql, new { documentId }, uow.Transaction, cancellationToken: ct));
        }

        public async Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default)
        {
            uow.EnsureActiveTransaction();

            var sql = $"DELETE FROM {TypedTable} WHERE document_id = @documentId;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(sql, new { documentId }, uow.Transaction, cancellationToken: ct));
        }

        public async Task UpdateDraftAsync(DocumentRecord updatedDraft, CancellationToken ct = default)
        {
            uow.EnsureActiveTransaction();

            var sql = $"""
UPDATE {TypedTable}
SET update_calls = update_calls + 1,
    last_status = @status,
    last_marked_for_deletion_at_utc = @markedAt
WHERE document_id = @documentId;
""";

            await uow.Connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    documentId = updatedDraft.Id,
                    status = updatedDraft.Status.ToString(),
                    markedAt = updatedDraft.MarkedForDeletionAtUtc
                },
                uow.Transaction,
                cancellationToken: ct));
        }
    }
}
