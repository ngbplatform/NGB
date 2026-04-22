using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Documents.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;
using NGB.Definitions;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentDraftService_ExternalTransactionMode_TypedStorageFailure_RollsBack_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    // IMPORTANT:
    // Use unique typeCode/table names to avoid colliding with real module typed tables.
    private const string TypeCode = "it_doc_ts_ext";
    private const string TypedTable = "doc_it_doc_ts_ext";

    [Fact]
    public async Task CreateDraftAsync_ManageTransactionFalse_WhenTypedStorageThrows_ExternalRollback_RemovesRegistryAndTypedRows()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        await EnsureTypedTableExistsAndEmptyAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>();
                services.AddScoped<FailAfterInsertDocumentTypeStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<FailAfterInsertDocumentTypeStorage>());
            });

        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var dateUtc = new DateTime(2026, 01, 15, 12, 00, 00, DateTimeKind.Utc);

        // Act
        await uow.BeginTransactionAsync();
        try
        {
            var act = () => drafts.CreateDraftAsync(TypeCode, number: "D-EXT-ERR", dateUtc, manageTransaction: false);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*simulated storage failure (external tx)*");
        }
        finally
        {
            await uow.RollbackAsync();
        }

        // Assert
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var docCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM documents WHERE type_code = @t AND number = @n;",
            new { t = TypeCode, n = "D-EXT-ERR" });

        docCount.Should().Be(0, "external rollback must undo document registry insert");

        var typedCount = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {TypedTable};");
        typedCount.Should().Be(0, "external rollback must undo typed storage insert");
    }

    private static async Task EnsureTypedTableExistsAndEmptyAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var ddl = $"""
CREATE TABLE IF NOT EXISTS {TypedTable} (
    document_id UUID PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    amount NUMERIC(19,4) NOT NULL DEFAULT 0,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
TRUNCATE TABLE {TypedTable};
""";

        await conn.ExecuteAsync(ddl);
    }

    sealed class FailAfterInsertDocumentTypeStorage(IUnitOfWork uow) : IDocumentTypeStorage
    {
        public string TypeCode => DocumentDraftService_ExternalTransactionMode_TypedStorageFailure_RollsBack_P0Tests.TypeCode;

        public async Task CreateDraftAsync(Guid documentId, CancellationToken ct = default)
        {
            uow.EnsureActiveTransaction();

            var sql = $"INSERT INTO {TypedTable} (document_id, amount) VALUES (@documentId, 0) ON CONFLICT (document_id) DO NOTHING;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(sql, new { documentId }, uow.Transaction, cancellationToken: ct));

            throw new NotSupportedException("simulated storage failure (external tx)");
        }

        public Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
