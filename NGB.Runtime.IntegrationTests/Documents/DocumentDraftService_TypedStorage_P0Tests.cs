using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Definitions;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentDraftService_TypedStorage_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    // IMPORTANT:
    // Use unique typeCode/table names to avoid colliding with real module typed tables (Demo.Trade etc.)
    // that may already exist in the shared integration-test database.
    private const string TypeCode = "it_doc_ts";
    private const string TypedTable = "doc_it_doc_ts";

    [Fact]
    public async Task CreateDraftAsync_WithTypedStorage_CreatesRegistryRowAndTypedRow()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>();
                services.AddScoped<ItDocumentTypeStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<ItDocumentTypeStorage>());
            });

        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var dateUtc = new DateTime(2026, 01, 15, 12, 00, 00, DateTimeKind.Utc);

        var id = await drafts.CreateDraftAsync(TypeCode, number: "D-1", dateUtc);

        var doc = await repo.GetAsync(id);
        doc.Should().NotBeNull();
        doc!.Id.Should().Be(id);
        doc.TypeCode.Should().Be(TypeCode);
        doc.Number.Should().Be("D-1");
        doc.DateUtc.Should().Be(dateUtc);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var typedCount = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {TypedTable} WHERE document_id = @id;",
            new { id });

        typedCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateDraftAsync_WhenTypedStorageThrows_RollsBackRegistryAndTyped()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

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

        var dateUtc = new DateTime(2026, 01, 15, 12, 00, 00, DateTimeKind.Utc);

        var act = () => drafts.CreateDraftAsync(TypeCode, number: "D-ERR", dateUtc);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*simulated storage failure*");

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        // Registry row should not exist (transaction rolled back).
        var docCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM documents WHERE type_code = @t AND number = @n;",
            new { t = TypeCode, n = "D-ERR" });

        docCount.Should().Be(0);

        // Typed row should also be rolled back.
        var typedCount = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {TypedTable};");
        typedCount.Should().Be(0);
    }

    [Fact]
    public async Task CreateDraftAsync_WithoutTypedStorage_StillCreatesRegistryRow_AndDoesNotInsertTypedRow()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());

        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var dateUtc = new DateTime(2026, 01, 15, 12, 00, 00, DateTimeKind.Utc);

        var id = await drafts.CreateDraftAsync(TypeCode, number: "D-NO-STORAGE", dateUtc);

        var doc = await repo.GetAsync(id);
        doc.Should().NotBeNull();
        doc!.TypeCode.Should().Be(TypeCode);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var typedCount = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {TypedTable} WHERE document_id = @id;",
            new { id });

        typedCount.Should().Be(0);
    }

    [Fact]
    public async Task CreateDraftAsync_ManageTransactionFalse_RequiresActiveTransaction()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>();
                services.AddScoped<ItDocumentTypeStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<ItDocumentTypeStorage>());
            });

        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var dateUtc = new DateTime(2026, 01, 15, 12, 00, 00, DateTimeKind.Utc);

        // No active transaction -> must fail fast.
        var act = () => drafts.CreateDraftAsync(TypeCode, number: "D-NTX-ERR", dateUtc, manageTransaction: false);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("This operation requires an active transaction.");

        // With a transaction -> should succeed.
        await uow.BeginTransactionAsync();
        var id = await drafts.CreateDraftAsync(TypeCode, number: "D-NTX", dateUtc, manageTransaction: false);
        await uow.CommitAsync();

        var doc = await repo.GetAsync(id);
        doc.Should().NotBeNull();

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var typedCount = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {TypedTable} WHERE document_id = @id;",
            new { id });

        typedCount.Should().Be(1);
    }

    private static async Task EnsureTypedTableExistsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // Unique test table to avoid collisions with real module tables.
        var ddl = $"""
CREATE TABLE IF NOT EXISTS {TypedTable} (
    document_id UUID PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
    amount NUMERIC(19,4) NOT NULL DEFAULT 0,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
""";

        await conn.ExecuteAsync(ddl);
    }

    sealed class ItDocumentTypeStorage(IUnitOfWork uow) : IDocumentTypeStorage
    {
        public string TypeCode => DocumentDraftService_TypedStorage_P0Tests.TypeCode;

        public async Task CreateDraftAsync(Guid documentId, CancellationToken ct = default)
        {
            uow.EnsureActiveTransaction();

            var sql = $"INSERT INTO {TypedTable} (document_id, amount) VALUES (@documentId, 0) ON CONFLICT (document_id) DO NOTHING;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(sql, new { documentId }, uow.Transaction, cancellationToken: ct));
        }

        public async Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default)
        {
            uow.EnsureActiveTransaction();

            var sql = $"DELETE FROM {TypedTable} WHERE document_id = @documentId;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(sql, new { documentId }, uow.Transaction, cancellationToken: ct));
        }
    }

    sealed class FailAfterInsertDocumentTypeStorage(IUnitOfWork uow) : IDocumentTypeStorage
    {
        public string TypeCode => DocumentDraftService_TypedStorage_P0Tests.TypeCode;

        public async Task CreateDraftAsync(Guid documentId, CancellationToken ct = default)
        {
            uow.EnsureActiveTransaction();

            var sql = $"INSERT INTO {TypedTable} (document_id, amount) VALUES (@documentId, 0) ON CONFLICT (document_id) DO NOTHING;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(sql, new { documentId }, uow.Transaction, cancellationToken: ct));

            throw new NotSupportedException("simulated storage failure");
        }

        public Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
