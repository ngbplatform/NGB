using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Documents.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Documents.Workflow;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentDraftService_DeleteDraft_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string TypeCode = "it_doc_del";
    private const string TypedTable = "doc_it_doc_del";

    [Fact]
    public async Task DeleteDraftAsync_WithTypedStorage_DeletesTypedAndRegistry_AndWritesAudit()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>();
                services.AddScoped<ItDeleteDraftTypeStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<ItDeleteDraftTypeStorage>());
            });

        var dateUtc = new DateTime(2026, 01, 15, 12, 00, 00, DateTimeKind.Utc);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            documentId = await drafts.CreateDraftAsync(TypeCode, number: "D-DEL-1", dateUtc);
            var deleted = await drafts.DeleteDraftAsync(documentId);

            deleted.Should().BeTrue();
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var docCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM documents WHERE id = @id;",
                new { id = documentId });

            docCount.Should().Be(0);

            var typedCount = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM {TypedTable} WHERE document_id = @id;",
                new { id = documentId });

            typedCount.Should().Be(0);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    ActionCode: AuditActionCodes.DocumentDeleteDraft,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            events.Single().Changes.Select(c => c.FieldPath)
                .Should()
                .Contain(new[] { "type_code", "date_utc", "status" });
        }
    }

    [Fact]
    public async Task DeleteDraftAsync_Twice_IsIdempotent_AndDoesNotWriteSecondAuditEvent()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>();
                services.AddScoped<ItDeleteDraftTypeStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<ItDeleteDraftTypeStorage>());
            });

        var dateUtc = new DateTime(2026, 01, 15, 12, 00, 00, DateTimeKind.Utc);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            documentId = await drafts.CreateDraftAsync(TypeCode, number: "D-DEL-2", dateUtc);

            (await drafts.DeleteDraftAsync(documentId)).Should().BeTrue();
            (await drafts.DeleteDraftAsync(documentId)).Should().BeFalse(); // idempotent no-op
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    ActionCode: AuditActionCodes.DocumentDeleteDraft,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task DeleteDraftAsync_WhenPosted_Throws_AndDoesNotDeleteAnything()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>();
                services.AddScoped<ItDeleteDraftTypeStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<ItDeleteDraftTypeStorage>());
            });

        var now = DateTime.UtcNow;

        var documentId = Guid.CreateVersion7();

        // Create a posted registry row directly (service should refuse to delete it).
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            const string insert = """
INSERT INTO documents(
    id, type_code, number, date_utc,
    status, posted_at_utc, marked_for_deletion_at_utc,
    created_at_utc, updated_at_utc
)
VALUES (
    @Id, @TypeCode, @Number, @DateUtc,
    @Status, @PostedAtUtc, NULL,
    @CreatedAtUtc, @UpdatedAtUtc
);
""";

            await conn.ExecuteAsync(insert, new
            {
                Id = documentId,
                TypeCode = TypeCode,
                Number = "POSTED-1",
                DateUtc = new DateTime(2026, 01, 10, 0, 0, 0, DateTimeKind.Utc),
                Status = (short)DocumentStatus.Posted,
                PostedAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

            (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM documents WHERE id = @id;", new { id = documentId }))
                .Should()
                .Be(1);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            var act = () => drafts.DeleteDraftAsync(documentId);

            var ex = await act.Should().ThrowAsync<DocumentWorkflowStateMismatchException>();
            ex.Which.ErrorCode.Should().Be(DocumentWorkflowStateMismatchException.ErrorCodeConst);
            ex.Which.Context.Should().ContainKey("operation").WhoseValue.Should().Be("DocumentDraft.DeleteDraft");
            ex.Which.Context.Should().ContainKey("expectedState").WhoseValue.Should().Be("Draft or MarkedForDeletion");
            ex.Which.Context.Should().ContainKey("actualState").WhoseValue.Should().Be(DocumentStatus.Posted.ToString());
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM documents WHERE id = @id;", new { id = documentId }))
                .Should()
                .Be(1);

            (await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {TypedTable} WHERE document_id = @id;", new { id = documentId }))
                .Should()
                .Be(0);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    ActionCode: AuditActionCodes.DocumentDeleteDraft,
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

        // FK RESTRICT/NO ACTION to ensure the delete flow is correct:
        // typed row must be deleted BEFORE deleting documents(id).
        var ddl = $"""
CREATE TABLE IF NOT EXISTS {TypedTable} (
    document_id UUID PRIMARY KEY REFERENCES documents(id) ON DELETE RESTRICT,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
""";

        await conn.ExecuteAsync(ddl);
    }

    private sealed class ItDeleteDraftTypeStorage(IUnitOfWork uow) : IDocumentTypeStorage
    {
        public string TypeCode => DocumentDraftService_DeleteDraft_P0Tests.TypeCode;

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
