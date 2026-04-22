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
public sealed class DocumentDraftService_NumberingOnCreate_FullHookFailure_RollsBack_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    // IMPORTANT:
    // Use unique typeCode/table names to avoid colliding with real module typed tables.
    private const string TypeCode = "it_doc_num_create_rb";
    private const string TypedTable = "doc_it_doc_num_create_rb";

    [Fact]
    public async Task CreateDraftAsync_WhenNumberIsAssignedOnCreateDraft_AndFullUpdateHookThrows_RollsBackEverything()
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

        var date = new DateTime(2026, 01, 10, 0, 0, 0, DateTimeKind.Utc);

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            var act = () => drafts.CreateDraftAsync(TypeCode, number: null, dateUtc: date, manageTransaction: true, ct: CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*simulated full update hook failure*");
        }

        // Assert
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var docCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM documents WHERE type_code = @t;",
            new { t = TypeCode });
        docCount.Should().Be(0, "CreateDraftAsync must be fully transactional and rollback registry row on failure");

        var typedCount = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {TypedTable};");
        typedCount.Should().Be(0, "typed storage row must also rollback");

        var seqCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM document_number_sequences WHERE type_code = @t AND fiscal_year = @y;",
            new { t = TypeCode, y = 2026 });
        seqCount.Should().Be(0, "number sequences must not advance outside the transaction (no gaps on rollback)");
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
                    new DocumentPresentationMetadata("IT Doc NumberOnCreate Rollback"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .TypedStorage<ItDocStorage>()
                .NumberingPolicy<ItDocNumberingPolicy>());
        }
    }

    private sealed class ItDocNumberingPolicy : IDocumentNumberingPolicy
    {
        public string TypeCode => DocumentDraftService_NumberingOnCreate_FullHookFailure_RollsBack_P0Tests.TypeCode;
        public bool EnsureNumberOnCreateDraft => true;
        public bool EnsureNumberOnPost => false;
    }

    private sealed class ItDocStorage(IUnitOfWork uow)
        : IDocumentTypeStorage, IDocumentTypeDraftFullUpdater
    {
        public string TypeCode => DocumentDraftService_NumberingOnCreate_FullHookFailure_RollsBack_P0Tests.TypeCode;

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
