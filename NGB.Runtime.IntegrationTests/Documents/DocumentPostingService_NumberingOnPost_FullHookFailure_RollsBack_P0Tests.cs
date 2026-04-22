using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Posting;
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
public sealed class DocumentPostingService_NumberingOnPost_FullHookFailure_RollsBack_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    // IMPORTANT:
    // Use unique typeCode/table names to avoid colliding with real module typed tables.
    private const string TypeCode = "it_doc_num_post_rb";
    private const string TypedTable = "doc_it_doc_num_post_rb";

    [Fact]
    public async Task PostAsync_WhenNumberIsAssignedOnPost_AndFullUpdateHookThrows_RollsBackNumber_Status_AndPostingSideEffects()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItDocContributor>();
                services.AddSingleton<ItDocNumberingPolicy>();
                services.AddScoped<ItDocStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<ItDocStorage>());
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

            var act = () => posting.PostAsync(
                id,
                async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(id, date, chart.Get("50"), chart.Get("90.1"), 10m);
                },
                CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*simulated full update hook failure*");
        }

        // Assert
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var doc = await conn.QuerySingleAsync<(string? Number, short Status, DateTime? PostedAtUtc)>(
            "SELECT number AS Number, status AS Status, posted_at_utc AS PostedAtUtc FROM documents WHERE id = @id;",
            new { id });

        doc.Number.Should().BeNull("number assignment on post must be transactional and rollback on failure");
        doc.Status.Should().Be((short)DocumentStatus.Draft);
        doc.PostedAtUtc.Should().BeNull();

        var typed = await conn.QuerySingleAsync<(int UpdateCalls, string? LastNumber)>(
            $"SELECT update_calls AS UpdateCalls, last_number AS LastNumber FROM {TypedTable} WHERE document_id = @id;",
            new { id });

        typed.UpdateCalls.Should().Be(0, "typed full update write must rollback together with the failed PostAsync");
        typed.LastNumber.Should().BeNull();

        var seqCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM document_number_sequences WHERE type_code = @t AND fiscal_year = @y;",
            new { t = TypeCode, y = 2026 });
        seqCount.Should().Be(0, "number sequences must not advance outside the transaction (no gaps on rollback)");

        var postingLogCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_posting_state WHERE document_id = @id AND operation = 1;",
            new { id });
        postingLogCount.Should().Be(0);

        var registerCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_register_main WHERE document_id = @id;",
            new { id });
        registerCount.Should().Be(0);
    }

    private static async Task EnsureTypedTableExistsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var ddl = $"""
CREATE TABLE IF NOT EXISTS {TypedTable} (
    document_id UUID PRIMARY KEY REFERENCES documents(id) ON DELETE RESTRICT,
    update_calls INT NOT NULL DEFAULT 0,
    last_number TEXT NULL
);
""";

        await conn.ExecuteAsync(ddl);
    }

    private sealed class ItDocContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(TypeCode, b => b
                .Metadata(new DocumentTypeMetadata(
                    TypeCode,
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Doc NumberOnPost Rollback"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .TypedStorage<ItDocStorage>()
                .NumberingPolicy<ItDocNumberingPolicy>());
        }
    }

    private sealed class ItDocNumberingPolicy : IDocumentNumberingPolicy
    {
        public string TypeCode => DocumentPostingService_NumberingOnPost_FullHookFailure_RollsBack_P0Tests.TypeCode;
        public bool EnsureNumberOnCreateDraft => false;
        public bool EnsureNumberOnPost => true;
    }

    private sealed class ItDocStorage(IUnitOfWork uow)
        : IDocumentTypeStorage, IDocumentTypeDraftFullUpdater
    {
        public string TypeCode => DocumentPostingService_NumberingOnPost_FullHookFailure_RollsBack_P0Tests.TypeCode;

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

            // Simulate a write + failure to validate rollback behavior.
            var sql = $"""
UPDATE {TypedTable}
SET update_calls = update_calls + 1,
    last_number = @number
WHERE document_id = @documentId;
""";

            await uow.Connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    documentId = updatedDraft.Id,
                    number = updatedDraft.Number
                },
                uow.Transaction,
                cancellationToken: ct));

            throw new NotSupportedException("simulated full update hook failure");
        }
    }
}
