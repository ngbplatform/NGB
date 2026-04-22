using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Definitions;
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
public sealed class DocumentDraftService_DeleteDraft_ExternalTransactionMode_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    // Use a dedicated typed table to avoid schema coupling with other tests.
    private const string TypeCode = "it_doc_del";
    private const string TypedTable = "doc_it_doc_del_ext";

    [Fact]
    public async Task DeleteDraftAsync_ManageTransactionFalse_WithoutActiveTransaction_Throws_AndDoesNotDelete()
    {
        await EnsureTypedTableExistsAndEmptyAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>();
                services.AddScoped<ExternalTxDeleteDraftTypeStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<ExternalTxDeleteDraftTypeStorage>());
            });

        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        var dateUtc = new DateTime(2026, 01, 15, 12, 00, 00, DateTimeKind.Utc);
        var documentId = await drafts.CreateDraftAsync(TypeCode, number: "D-EXT-DEL-1", dateUtc);

        var act = () => drafts.DeleteDraftAsync(documentId, manageTransaction: false);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("This operation requires an active transaction.");

        // Still present.
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM documents WHERE id = @id;", new { id = documentId }))
            .Should().Be(1);

        (await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {TypedTable} WHERE document_id = @id;", new { id = documentId }))
            .Should().Be(1);

        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();
        var events = await reader.QueryAsync(
            new AuditLogQuery(
                EntityKind: AuditEntityKind.Document,
                EntityId: documentId,
                ActionCode: AuditActionCodes.DocumentDeleteDraft,
                Limit: 20,
                Offset: 0),
            CancellationToken.None);

        events.Should().BeEmpty("delete draft must not be logged when the operation fails fast before doing any work");
    }

    [Fact]
    public async Task DeleteDraftAsync_ManageTransactionFalse_InsideExternalTransaction_Rollback_RestoresRows_AndNoAudit()
    {
        await EnsureTypedTableExistsAndEmptyAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>();
                services.AddScoped<ExternalTxDeleteDraftTypeStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<ExternalTxDeleteDraftTypeStorage>());
            });

        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var dateUtc = new DateTime(2026, 01, 15, 12, 00, 00, DateTimeKind.Utc);
        var documentId = await drafts.CreateDraftAsync(TypeCode, number: "D-EXT-DEL-2", dateUtc);

        await uow.BeginTransactionAsync();
        try
        {
            var deleted = await drafts.DeleteDraftAsync(documentId, manageTransaction: false);
            deleted.Should().BeTrue();

            // Inside the transaction, the rows should appear deleted.
            var docCountInTx = await uow.Connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    "SELECT COUNT(*) FROM documents WHERE id = @id;",
                    new { id = documentId },
                    uow.Transaction));

            docCountInTx.Should().Be(0);

            var typedCountInTx = await uow.Connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    $"SELECT COUNT(*) FROM {TypedTable} WHERE document_id = @id;",
                    new { id = documentId },
                    uow.Transaction));

            typedCountInTx.Should().Be(0);

            // External transaction semantics: service must not commit.
            await uow.RollbackAsync();
        }
        finally
        {
            if (uow.HasActiveTransaction)
                await uow.RollbackAsync();
        }

        // After rollback, the rows must be present again.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM documents WHERE id = @id;", new { id = documentId }))
                .Should().Be(1);

            (await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {TypedTable} WHERE document_id = @id;", new { id = documentId }))
                .Should().Be(1);
        }

        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();
        var events = await reader.QueryAsync(
            new AuditLogQuery(
                EntityKind: AuditEntityKind.Document,
                EntityId: documentId,
                ActionCode: AuditActionCodes.DocumentDeleteDraft,
                Limit: 20,
                Offset: 0),
            CancellationToken.None);

        events.Should().BeEmpty("external rollback must undo audit writes");
    }

    private static async Task EnsureTypedTableExistsAndEmptyAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var ddl = $"""
CREATE TABLE IF NOT EXISTS {TypedTable} (
    document_id UUID PRIMARY KEY REFERENCES documents(id) ON DELETE RESTRICT,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
TRUNCATE TABLE {TypedTable};
""";

        await conn.ExecuteAsync(ddl);
    }

    private sealed class ExternalTxDeleteDraftTypeStorage(IUnitOfWork uow) : IDocumentTypeStorage
    {
        public string TypeCode => DocumentDraftService_DeleteDraft_ExternalTransactionMode_P0Tests.TypeCode;

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
    }
}
