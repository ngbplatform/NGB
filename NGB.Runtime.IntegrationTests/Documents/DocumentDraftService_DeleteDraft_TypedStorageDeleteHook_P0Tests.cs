using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Definitions;
using NGB.Definitions.Documents.Numbering;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Documents.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentDraftService_DeleteDraft_TypedStorageDeleteHook_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    // IMPORTANT: use unique names to avoid colliding with real module typed tables.
    private const string TypeCode = "it_doc_del_hook";
    private const string TypedTable = "doc_it_doc_del_hook";

    [Fact]
    public async Task DeleteDraftAsync_WhenTypedStorageHasRestrictFk_DeletesTypedRowAndDocumentRow()
    {
        await Fixture.ResetDatabaseAsync();
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItDocDeleteContributor>();
                services.AddSingleton<ItDocDeleteNumberingPolicy>();
                services.AddScoped<ItDocDeleteStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<ItDocDeleteStorage>());
            });

        var date = new DateTime(2026, 01, 10, 0, 0, 0, DateTimeKind.Utc);
        Guid id;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            id = await drafts.CreateDraftAsync(TypeCode, number: "D-1", dateUtc: date, manageTransaction: true, ct: CancellationToken.None);
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();
            (await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {TypedTable} WHERE document_id = @id;", new { id }))
                .Should().Be(1);
        }

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            (await drafts.DeleteDraftAsync(id, manageTransaction: true, ct: CancellationToken.None)).Should().BeTrue();
        }

        // Assert
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM documents WHERE id = @id;", new { id }))
                .Should().Be(0);

            (await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {TypedTable} WHERE document_id = @id;", new { id }))
                .Should().Be(0);

            (await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_audit_events WHERE entity_id = @id AND action_code = @code;",
                new { id, code = AuditActionCodes.DocumentDeleteDraft }))
                .Should().Be(1);
        }
    }

    [Fact]
    public async Task DeleteDraftAsync_WhenTypedDeleteThrows_RollsBack_DoesNotDeleteDocument_AndDoesNotWriteAudit()
    {
        await Fixture.ResetDatabaseAsync();
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItDocDeleteThrowingContributor>();
                services.AddSingleton<ItDocDeleteNumberingPolicy>();
                services.AddScoped<ItDocDeleteStorageThatThrows>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<ItDocDeleteStorageThatThrows>());
            });

        var date = new DateTime(2026, 01, 10, 0, 0, 0, DateTimeKind.Utc);
        Guid id;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            id = await drafts.CreateDraftAsync(TypeCode, number: "D-1", dateUtc: date, manageTransaction: true, ct: CancellationToken.None);
        }

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            var act = async () => await drafts.DeleteDraftAsync(id, manageTransaction: true, ct: CancellationToken.None);
            await act.Should().ThrowAsync<NotSupportedException>().WithMessage("boom: typed delete");
        }

        // Assert: everything rolled back
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM documents WHERE id = @id;", new { id }))
                .Should().Be(1);

            (await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {TypedTable} WHERE document_id = @id;", new { id }))
                .Should().Be(1);

            (await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_audit_events WHERE entity_id = @id AND action_code = @code;",
                new { id, code = AuditActionCodes.DocumentDeleteDraft }))
                .Should().Be(0);
        }
    }

    private static async Task EnsureTypedTableExistsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var ddl = $"""
CREATE TABLE IF NOT EXISTS {TypedTable} (
    document_id UUID PRIMARY KEY REFERENCES documents(id) ON DELETE RESTRICT
);
""";

        await conn.ExecuteAsync(ddl);
    }

    private sealed class ItDocDeleteContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(TypeCode, b => b
                .Metadata(new DocumentTypeMetadata(
                    TypeCode,
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Doc Delete Hook"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .TypedStorage<ItDocDeleteStorage>()
                .NumberingPolicy<ItDocDeleteNumberingPolicy>());
        }
    }

    private sealed class ItDocDeleteThrowingContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(TypeCode, b => b
                .Metadata(new DocumentTypeMetadata(
                    TypeCode,
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Doc Delete Hook (Throwing)"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .TypedStorage<ItDocDeleteStorageThatThrows>()
                .NumberingPolicy<ItDocDeleteNumberingPolicy>());
        }
    }

    private sealed class ItDocDeleteNumberingPolicy : IDocumentNumberingPolicy
    {
        public string TypeCode => DocumentDraftService_DeleteDraft_TypedStorageDeleteHook_P0Tests.TypeCode;
        public bool EnsureNumberOnCreateDraft => false;
        public bool EnsureNumberOnPost => false;
    }

    private sealed class ItDocDeleteStorage(IUnitOfWork uow) : IDocumentTypeStorage
    {
        public string TypeCode => DocumentDraftService_DeleteDraft_TypedStorageDeleteHook_P0Tests.TypeCode;

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

    private sealed class ItDocDeleteStorageThatThrows(IUnitOfWork uow) : IDocumentTypeStorage
    {
        public string TypeCode => DocumentDraftService_DeleteDraft_TypedStorageDeleteHook_P0Tests.TypeCode;

        public async Task CreateDraftAsync(Guid documentId, CancellationToken ct = default)
        {
            uow.EnsureActiveTransaction();
            var sql = $"INSERT INTO {TypedTable} (document_id) VALUES (@documentId) ON CONFLICT (document_id) DO NOTHING;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(sql, new { documentId }, uow.Transaction, cancellationToken: ct));
        }

        public async Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default)
        {
            uow.EnsureActiveTransaction();

            // Simulate a real typed-storage deletion that fails after touching data.
            var sql = $"DELETE FROM {TypedTable} WHERE document_id = @documentId;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(sql, new { documentId }, uow.Transaction, cancellationToken: ct));

            throw new NotSupportedException("boom: typed delete");
        }
    }
}