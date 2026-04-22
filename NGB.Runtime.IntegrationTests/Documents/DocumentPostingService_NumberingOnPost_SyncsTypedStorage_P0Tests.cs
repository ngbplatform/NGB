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
using NGB.Runtime.IntegrationTests.Reporting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_NumberingOnPost_SyncsTypedStorage_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    // IMPORTANT:
    // Use unique typeCode/table names to avoid colliding with real module typed tables.
    private const string TypeCode = "it_doc_post_num";
    private const string TypedTable = "doc_it_doc_post_num";

    [Fact]
    public async Task PostAsync_WhenNumberIsAssignedOnPost_InvokesTypedFullUpdateHookToSyncTypedStorage()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItDocPostNumberContributor>();
                services.AddSingleton<ItDocPostNumberNumberingPolicy>();
                services.AddScoped<ItDocPostNumberStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<ItDocPostNumberStorage>());
            });

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var date = new DateTime(2026, 01, 10, 0, 0, 0, DateTimeKind.Utc);

        Guid id;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            id = await drafts.CreateDraftAsync(TypeCode, number: null, dateUtc: date, manageTransaction: true, ct: CancellationToken.None);
        }

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.PostAsync(
                id,
                async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(id, date, chart.Get("50"), chart.Get("90.1"), 10m);
                },
                CancellationToken.None);
        }

        // Assert
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var doc = await conn.QuerySingleAsync<(string? Number, short Status)>(
                "SELECT number AS Number, status AS Status FROM documents WHERE id = @id;",
                new { id });

            doc.Number.Should().NotBeNullOrWhiteSpace();
            doc.Status.Should().Be((short)DocumentStatus.Posted);

            var row = await conn.QuerySingleAsync<(int UpdateCalls, string? LastNumber, DateTime? LastDateUtc)>(
                $"SELECT update_calls AS UpdateCalls, last_number AS LastNumber, last_date_utc AS LastDateUtc FROM {TypedTable} WHERE document_id = @id;",
                new { id });

            row.UpdateCalls.Should().Be(1);
            row.LastNumber.Should().Be(doc.Number);
            row.LastDateUtc.Should().Be(date);
        }
    }

    [Fact]
    public async Task PostAsync_WhenDraftAlreadyHasNumber_DoesNotInvokeTypedUpdateHookAsPartOfEnsureNumberOnPost()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItDocPostNumberContributor>();
                services.AddSingleton<ItDocPostNumberNumberingPolicy>();
                services.AddScoped<ItDocPostNumberStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<ItDocPostNumberStorage>());
            });

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var date = new DateTime(2026, 01, 10, 0, 0, 0, DateTimeKind.Utc);

        Guid id;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            id = await drafts.CreateDraftAsync(TypeCode, number: "MAN-1", dateUtc: date, manageTransaction: true, ct: CancellationToken.None);
        }

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.PostAsync(
                id,
                async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(id, date, chart.Get("50"), chart.Get("90.1"), 10m);
                },
                CancellationToken.None);
        }

        // Assert
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var row = await conn.QuerySingleAsync<(int UpdateCalls, string? LastNumber)>(
                $"SELECT update_calls AS UpdateCalls, last_number AS LastNumber FROM {TypedTable} WHERE document_id = @id;",
                new { id });

            row.UpdateCalls.Should().Be(0);
            row.LastNumber.Should().BeNull();
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

    private sealed class ItDocPostNumberContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(TypeCode, b => b
                .Metadata(new DocumentTypeMetadata(
                    TypeCode,
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Doc Post Number"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .TypedStorage<ItDocPostNumberStorage>()
                .NumberingPolicy<ItDocPostNumberNumberingPolicy>());
        }
    }

    private sealed class ItDocPostNumberNumberingPolicy : IDocumentNumberingPolicy
    {
        public string TypeCode => DocumentPostingService_NumberingOnPost_SyncsTypedStorage_P0Tests.TypeCode;
        public bool EnsureNumberOnCreateDraft => false;
        public bool EnsureNumberOnPost => true;
    }

    private sealed class ItDocPostNumberStorage(IUnitOfWork uow)
        : IDocumentTypeStorage, IDocumentTypeDraftFullUpdater
    {
        public string TypeCode => DocumentPostingService_NumberingOnPost_SyncsTypedStorage_P0Tests.TypeCode;

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

            await uow.Connection.ExecuteAsync(new CommandDefinition(
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
