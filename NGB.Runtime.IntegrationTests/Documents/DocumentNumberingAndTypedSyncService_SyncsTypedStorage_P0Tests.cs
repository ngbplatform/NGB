using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents.Numbering;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Runtime.UnitOfWork;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentNumberingAndTypedSyncService_SyncsTypedStorage_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string TypeCode = "it_doc_num_sync";
    private const string TypedTable = "doc_it_doc_num_sync";

    [Fact]
    public async Task EnsureNumberAndSyncTypedAsync_WhenNumberIsAssigned_SynchronizesTypedStorageWithAssignedNumber()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItDocNumSyncContributor>();
                services.AddScoped<ItDocNumSyncStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<ItDocNumSyncStorage>());
            });

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var dateUtc = new DateTime(2026, 01, 10, 0, 0, 0, DateTimeKind.Utc);
        Guid id;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            id = await drafts.CreateDraftAsync(TypeCode, number: null, dateUtc: dateUtc, manageTransaction: true, ct: CancellationToken.None);
        }

        // Act
        var nowUtc = new DateTime(2026, 01, 10, 1, 2, 3, DateTimeKind.Utc);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var sync = scope.ServiceProvider.GetRequiredService<IDocumentNumberingAndTypedSyncService>();

            await uow.ExecuteInUowTransactionAsync(async ct =>
            {
                var locked = await documents.GetForUpdateAsync(id, ct)
                             ?? throw new XunitException("Document not found");

                await sync.EnsureNumberAndSyncTypedAsync(locked, nowUtc, ct);
            }, CancellationToken.None);
        }

        // Assert
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var doc = await conn.QuerySingleAsync<(string? Number, DateTime DateUtc)>(
                "SELECT number AS Number, date_utc AS DateUtc FROM documents WHERE id = @id;",
                new { id });

            doc.Number.Should().NotBeNullOrWhiteSpace();
            doc.DateUtc.Should().Be(dateUtc);

            var row = await conn.QuerySingleAsync<(int UpdateCalls, string? LastNumber, DateTime? LastDateUtc)>(
                $"SELECT update_calls AS UpdateCalls, last_number AS LastNumber, last_date_utc AS LastDateUtc FROM {TypedTable} WHERE document_id = @id;",
                new { id });

            row.UpdateCalls.Should().Be(1);
            row.LastNumber.Should().Be(doc.Number);
            row.LastDateUtc.Should().Be(dateUtc);
        }
    }

    [Fact]
    public async Task EnsureNumberAndSyncTypedAsync_WhenNumberAlreadySet_DoesNotTouchTypedStorage()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItDocNumSyncContributor>();
                services.AddScoped<ItDocNumSyncStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<ItDocNumSyncStorage>());
            });

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var dateUtc = new DateTime(2026, 01, 10, 0, 0, 0, DateTimeKind.Utc);
        Guid id;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            id = await drafts.CreateDraftAsync(TypeCode, number: "MANUAL-001", dateUtc: dateUtc, manageTransaction: true, ct: CancellationToken.None);
        }

        // Act
        var nowUtc = new DateTime(2026, 01, 10, 1, 2, 3, DateTimeKind.Utc);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var sync = scope.ServiceProvider.GetRequiredService<IDocumentNumberingAndTypedSyncService>();

            await uow.ExecuteInUowTransactionAsync(async ct =>
            {
                var locked = await documents.GetForUpdateAsync(id, ct)
                             ?? throw new XunitException("Document not found");

                locked.Number.Should().Be("MANUAL-001");

                await sync.EnsureNumberAndSyncTypedAsync(locked, nowUtc, ct);
            }, CancellationToken.None);
        }

        // Assert
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var doc = await conn.QuerySingleAsync<(string? Number, DateTime DateUtc)>(
                "SELECT number AS Number, date_utc AS DateUtc FROM documents WHERE id = @id;",
                new { id });

            doc.Number.Should().Be("MANUAL-001");
            doc.DateUtc.Should().Be(dateUtc);

            var row = await conn.QuerySingleAsync<(int UpdateCalls, string? LastNumber, DateTime? LastDateUtc)>(
                $"SELECT update_calls AS UpdateCalls, last_number AS LastNumber, last_date_utc AS LastDateUtc FROM {TypedTable} WHERE document_id = @id;",
                new { id });

            row.UpdateCalls.Should().Be(0);
            row.LastNumber.Should().BeNull();
            row.LastDateUtc.Should().BeNull();
        }
    }

    [Fact]
    public async Task EnsureNumberAndSyncTypedAsync_IsAtomic_WhenTypedStorageUpdateFails()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItDocNumSyncContributor>();
                services.AddScoped<ItDocNumSyncStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<ItDocNumSyncStorage>());
            });

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var dateUtc = new DateTime(2026, 01, 10, 0, 0, 0, DateTimeKind.Utc);
        Guid id;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            id = await drafts.CreateDraftAsync(TypeCode, number: null, dateUtc: dateUtc, manageTransaction: true, ct: CancellationToken.None);
        }

        ItDocNumSyncStorage.EnableThrowOnUpdate(id);

        try
        {
            // Act
            var nowUtc = new DateTime(2026, 01, 10, 1, 2, 3, DateTimeKind.Utc);

            await using var scope = host.Services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var sync = scope.ServiceProvider.GetRequiredService<IDocumentNumberingAndTypedSyncService>();

            var ex = await FluentActions.Invoking(() => uow.ExecuteInUowTransactionAsync(async ct =>
                {
                    var locked = await documents.GetForUpdateAsync(id, ct)
                                 ?? throw new XunitException("Document not found");

                    await sync.EnsureNumberAndSyncTypedAsync(locked, nowUtc, ct);
                }, CancellationToken.None))
                .Should().ThrowAsync<NotSupportedException>();

            ex.Which.Message.Should().Contain("IT TEST");
        }
        finally
        {
            ItDocNumSyncStorage.DisableThrowOnUpdate(id);
        }

        // Assert: transaction rolled back => number and typed sync not persisted.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var doc = await conn.QuerySingleAsync<(string? Number, DateTime DateUtc)>(
                "SELECT number AS Number, date_utc AS DateUtc FROM documents WHERE id = @id;",
                new { id });

            doc.Number.Should().BeNull();
            doc.DateUtc.Should().Be(dateUtc);

            var row = await conn.QuerySingleAsync<(int UpdateCalls, string? LastNumber, DateTime? LastDateUtc)>(
                $"SELECT update_calls AS UpdateCalls, last_number AS LastNumber, last_date_utc AS LastDateUtc FROM {TypedTable} WHERE document_id = @id;",
                new { id });

            row.UpdateCalls.Should().Be(0);
            row.LastNumber.Should().BeNull();
            row.LastDateUtc.Should().BeNull();
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

    private sealed class ItDocNumSyncContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(TypeCode, b => b
                .Metadata(new DocumentTypeMetadata(
                    TypeCode,
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Doc Number Sync"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .TypedStorage<ItDocNumSyncStorage>());
        }
    }

    private sealed class ItDocNumSyncStorage(IUnitOfWork uow)
        : IDocumentTypeStorage, IDocumentTypeDraftFullUpdater
    {
        private static readonly ConcurrentDictionary<Guid, byte> ThrowOnUpdate = new();

        public static void EnableThrowOnUpdate(Guid documentId) => ThrowOnUpdate.TryAdd(documentId, 0);
        public static void DisableThrowOnUpdate(Guid documentId) => ThrowOnUpdate.TryRemove(documentId, out _);

        public string TypeCode => DocumentNumberingAndTypedSyncService_SyncsTypedStorage_P0Tests.TypeCode;

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

            if (ThrowOnUpdate.ContainsKey(updatedDraft.Id))
                throw new NotSupportedException("IT TEST: typed draft update failed");
        }
    }
}
