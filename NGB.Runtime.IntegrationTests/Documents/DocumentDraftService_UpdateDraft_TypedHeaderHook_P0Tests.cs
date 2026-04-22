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
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentDraftService_UpdateDraft_TypedHeaderHook_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string TypeCode = "it_doc_hdr_hook";
    private const string TypedTable = "doc_it_doc_hdr_hook";

    [Fact]
    public async Task UpdateDraftAsync_WhenHeaderChanges_CallsTypedHook_AndWritesAudit()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>();
                services.AddSingleton<IDefinitionsContributor, ItDocHeaderHookContributor>();
                services.AddScoped<ItDocHeaderHookStorage>();
            });

        var date1 = new DateTime(2026, 01, 10, 0, 0, 0, DateTimeKind.Utc);
        var date2 = new DateTime(2026, 01, 11, 0, 0, 0, DateTimeKind.Utc);

        Guid id;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            id = await drafts.CreateDraftAsync(TypeCode, number: "N-1", dateUtc: date1, manageTransaction: true, ct: CancellationToken.None);
            var updated = await drafts.UpdateDraftAsync(id, number: "N-2", dateUtc: date2, manageTransaction: true, ct: CancellationToken.None);

            updated.Should().BeTrue();
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var row = await conn.QuerySingleAsync<TypedRow>(
                $"SELECT document_id AS DocumentId, update_calls AS UpdateCalls, last_number AS LastNumber, last_date_utc AS LastDateUtc FROM {TypedTable} WHERE document_id = @id;",
                new { id });

            row.UpdateCalls.Should().Be(1);
            row.LastNumber.Should().Be("N-2");
            row.LastDateUtc.Should().Be(date2);
        }

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

            events.Should().ContainSingle();
            events.Single().Changes.Select(c => c.FieldPath)
                .Should()
                .Contain(new[] { "number", "date_utc" });
        }
    }

    [Fact]
    public async Task UpdateDraftAsync_WhenTypedHookThrows_RollsBackHeader_AndDoesNotWriteAudit()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>();
                services.AddSingleton<IDefinitionsContributor, ItDocHeaderHookContributor>();
                services.AddScoped<ItDocHeaderHookStorage>();
            });

        var date1 = new DateTime(2026, 01, 10, 0, 0, 0, DateTimeKind.Utc);
        var date2 = new DateTime(2026, 01, 11, 0, 0, 0, DateTimeKind.Utc);

        Guid id;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            id = await drafts.CreateDraftAsync(TypeCode, number: "N-1", dateUtc: date1, manageTransaction: true, ct: CancellationToken.None);

            var act = async () =>
                await drafts.UpdateDraftAsync(id, number: ItDocHeaderHookStorage.ThrowNumber, dateUtc: date2, manageTransaction: true, ct: CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("Simulated typed draft update failure");
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var doc = await conn.QuerySingleAsync<(string? Number, DateTime DateUtc)>(
                "SELECT number AS Number, date_utc AS DateUtc FROM documents WHERE id = @id;",
                new { id });

            doc.Number.Should().Be("N-1");
            doc.DateUtc.Should().Be(date1);

            var row = await conn.QuerySingleAsync<TypedRow>(
                $"SELECT document_id AS DocumentId, update_calls AS UpdateCalls, last_number AS LastNumber, last_date_utc AS LastDateUtc FROM {TypedTable} WHERE document_id = @id;",
                new { id });

            row.UpdateCalls.Should().Be(0);
            row.LastNumber.Should().BeNull();
            row.LastDateUtc.Should().BeNull();
        }

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

    private sealed record TypedRow(Guid DocumentId, int UpdateCalls, string? LastNumber, DateTime? LastDateUtc);

    private sealed class ItDocHeaderHookContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(TypeCode, b => b
                .Metadata(new DocumentTypeMetadata(
                    TypeCode,
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Doc Header Hook"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .TypedStorage<ItDocHeaderHookStorage>());
        }
    }

    private sealed class ItDocHeaderHookStorage(IUnitOfWork uow)
        : IDocumentTypeStorage, IDocumentTypeDraftFullUpdater
    {
        public const string ThrowNumber = "BOOM";

        public string TypeCode => DocumentDraftService_UpdateDraft_TypedHeaderHook_P0Tests.TypeCode;

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

            if (string.Equals(updatedDraft.Number, ThrowNumber, StringComparison.Ordinal))
                throw new NotSupportedException("Simulated typed draft update failure");

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
