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
public sealed class DocumentDraftService_TypedFullUpdateHook_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    // IMPORTANT:
    // Use unique typeCode/table names to avoid colliding with real module typed tables.
    private const string TypeCode = "it_doc_full_upd";
    private const string TypedTable = "doc_it_doc_full_upd";

    [Fact]
    public async Task UpdateDraftAsync_WhenStorageImplementsTypedFullUpdateHook_InvokesHookWithUpdatedRecord()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItDocFullUpdateContributor>();
                services.AddSingleton<ItDocFullUpdateNumberingPolicy>();
                services.AddScoped<ItDocFullUpdateStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<ItDocFullUpdateStorage>());
            });

        var date1 = new DateTime(2026, 01, 10, 0, 0, 0, DateTimeKind.Utc);
        var date2 = new DateTime(2026, 01, 11, 0, 0, 0, DateTimeKind.Utc);

        Guid id;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            id = await drafts.CreateDraftAsync(TypeCode, number: "N-1", dateUtc: date1, manageTransaction: true, ct: CancellationToken.None);
        }

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            var updated = await drafts.UpdateDraftAsync(id, number: "N-2", dateUtc: date2, manageTransaction: true, ct: CancellationToken.None);
            updated.Should().BeTrue();
        }

        // Assert
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var row = await conn.QuerySingleAsync<(int UpdateCalls, string? LastNumber, DateTime? LastDateUtc, string? LastStatus)>(
                $"SELECT update_calls AS UpdateCalls, last_number AS LastNumber, last_date_utc AS LastDateUtc, last_status AS LastStatus FROM {TypedTable} WHERE document_id = @id;",
                new { id });

            row.UpdateCalls.Should().Be(1);
            row.LastNumber.Should().Be("N-2");
            row.LastDateUtc.Should().Be(date2);
            row.LastStatus.Should().Be(DocumentStatus.Draft.ToString());
        }
    }

    [Fact]
    public async Task CreateDraftAsync_WhenNumberingAssignsNumberOnCreateDraft_UpdatesTypedStorageViaFullHook()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItDocFullUpdateContributor>();
                services.AddSingleton<ItDocFullUpdateNumberingPolicy>();
                services.AddScoped<ItDocFullUpdateStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<ItDocFullUpdateStorage>());
            });

        var date = new DateTime(2026, 01, 10, 0, 0, 0, DateTimeKind.Utc);

        Guid id;
        string? assignedNumber;

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            id = await drafts.CreateDraftAsync(TypeCode, number: null, dateUtc: date, manageTransaction: true, ct: CancellationToken.None);
        }

        // Assert
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var doc = await conn.QuerySingleAsync<(string? Number, DateTime DateUtc)>(
                "SELECT number AS Number, date_utc AS DateUtc FROM documents WHERE id = @id;",
                new { id });

            doc.Number.Should().NotBeNullOrWhiteSpace();
            doc.DateUtc.Should().Be(date);
            assignedNumber = doc.Number;

            var row = await conn.QuerySingleAsync<(int UpdateCalls, string? LastNumber)>(
                $"SELECT update_calls AS UpdateCalls, last_number AS LastNumber FROM {TypedTable} WHERE document_id = @id;",
                new { id });

            // CreateDraftAsync creates typed draft row first (CreateDraftAsync) and then,
            // if numbering assigned a number, the platform invokes a typed update hook to keep it in sync.
            row.UpdateCalls.Should().Be(1);
            row.LastNumber.Should().Be(assignedNumber);
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
    last_date_utc TIMESTAMPTZ NULL,
    last_status TEXT NULL
);
""";

        await conn.ExecuteAsync(ddl);
    }

    private sealed class ItDocFullUpdateContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(TypeCode, b => b
                .Metadata(new DocumentTypeMetadata(
                    TypeCode,
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Doc Full Update"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .TypedStorage<ItDocFullUpdateStorage>()
                .NumberingPolicy<ItDocFullUpdateNumberingPolicy>());
        }
    }

    private sealed class ItDocFullUpdateNumberingPolicy : IDocumentNumberingPolicy
    {
        public string TypeCode => DocumentDraftService_TypedFullUpdateHook_P0Tests.TypeCode;
        public bool EnsureNumberOnCreateDraft => true;
        public bool EnsureNumberOnPost => false;
    }

    private sealed class ItDocFullUpdateStorage(IUnitOfWork uow)
        : IDocumentTypeStorage, IDocumentTypeDraftFullUpdater
    {
        public string TypeCode => DocumentDraftService_TypedFullUpdateHook_P0Tests.TypeCode;

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
    last_date_utc = @dateUtc,
    last_status = @status
WHERE document_id = @documentId;
""";

            await uow.Connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    documentId = updatedDraft.Id,
                    number = updatedDraft.Number,
                    dateUtc = updatedDraft.DateUtc,
                    status = updatedDraft.Status.ToString()
                },
                uow.Transaction,
                cancellationToken: ct));
        }
    }
}
