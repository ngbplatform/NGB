using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Documents.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentDraftService_UpdateDraft_ExternalTransaction_Rollback_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    // IMPORTANT:
    // Use unique typeCode/table names to avoid colliding with real module typed tables.
    private const string TypeCode = "it_doc_hdr_hook_ext";
    private const string TypedTable = "doc_it_doc_hdr_hook_ext";

    [Fact]
    public async Task UpdateDraftAsync_ManageTransactionFalse_WithoutActiveTransaction_Throws()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>();
                services.AddSingleton<IDefinitionsContributor, ItDocHeaderHookExtContributor>();
                services.AddScoped<ItDocHeaderHookExtStorage>();
            });

        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        // Act
        var act = () => drafts.UpdateDraftAsync(
            documentId: Guid.CreateVersion7(),
            number: "N",
            dateUtc: null,
            manageTransaction: false,
            ct: CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("This operation requires an active transaction.");
    }

    [Fact]
    public async Task UpdateDraftAsync_ManageTransactionFalse_WhenOuterTransactionRollsBack_DoesNotPersistHeader_TypedOrAudit()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>();
                services.AddSingleton<IDefinitionsContributor, ItDocHeaderHookExtContributor>();
                services.AddScoped<ItDocHeaderHookExtStorage>();
            });

        var date1 = new DateTime(2026, 01, 10, 0, 0, 0, DateTimeKind.Utc);
        var date2 = new DateTime(2026, 01, 11, 0, 0, 0, DateTimeKind.Utc);

        Guid id;

        // Create the draft in its own transaction.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            id = await drafts.CreateDraftAsync(TypeCode, number: "N-1", dateUtc: date1, manageTransaction: true, ct: CancellationToken.None);
        }

        // Act: update in external transaction mode and then rollback.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync();
            try
            {
                var updated = await drafts.UpdateDraftAsync(id, number: "N-2", dateUtc: date2, manageTransaction: false, ct: CancellationToken.None);
                updated.Should().BeTrue();
            }
            finally
            {
                await uow.RollbackAsync();
            }
        }

        // Assert: header not changed.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var doc = await conn.QuerySingleAsync<(string? Number, DateTime DateUtc)>(
                "SELECT number AS Number, date_utc AS DateUtc FROM documents WHERE id = @id;",
                new { id });

            doc.Number.Should().Be("N-1");
            doc.DateUtc.Should().Be(date1);

            var row = await conn.QuerySingleAsync<(int UpdateCalls, string? LastNumber, DateTime? LastDateUtc)>(
                $"SELECT update_calls AS UpdateCalls, last_number AS LastNumber, last_date_utc AS LastDateUtc FROM {TypedTable} WHERE document_id = @id;",
                new { id });

            row.UpdateCalls.Should().Be(0);
            row.LastNumber.Should().BeNull();
            row.LastDateUtc.Should().BeNull();
        }

        // Assert: audit not written.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: id,
                    ActionCode: AuditActionCodes.DocumentUpdateDraft,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().BeEmpty();
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
    last_number TEXT NULL,
    last_date_utc TIMESTAMPTZ NULL
);
""";

        await conn.ExecuteAsync(ddl);
    }

    private sealed class ItDocHeaderHookExtContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(TypeCode, b => b
                .Metadata(new DocumentTypeMetadata(
                    TypeCode,
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Doc Header Hook Ext"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .TypedStorage<ItDocHeaderHookExtStorage>());
        }
    }

    private sealed class ItDocHeaderHookExtStorage(IUnitOfWork uow)
        : IDocumentTypeStorage, IDocumentTypeDraftFullUpdater
    {
        public string TypeCode => DocumentDraftService_UpdateDraft_ExternalTransaction_Rollback_P0Tests.TypeCode;

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
    last_number = @number,
    last_date_utc = @dateUtc
WHERE document_id = @documentId;
""";

            await uow.Connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        documentId = updatedDraft.Id,
                        number = updatedDraft.Number,
                        dateUtc = updatedDraft.DateUtc
                    },
                    uow.Transaction,
                    cancellationToken: ct));
        }
    }
}
