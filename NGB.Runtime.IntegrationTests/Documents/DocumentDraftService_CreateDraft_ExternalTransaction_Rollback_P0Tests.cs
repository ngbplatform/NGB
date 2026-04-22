using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Definitions;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Documents.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentDraftService_CreateDraft_ExternalTransaction_Rollback_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    // IMPORTANT:
    // Use unique typeCode/table names to avoid colliding with real module typed tables.
    private const string TypeCode = "it_doc_create_ext";
    private const string TypedTable = "doc_it_doc_create_ext";

    [Fact]
    public async Task CreateDraftAsync_ManageTransactionFalse_WithoutActiveTransaction_Throws()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItDocCreateExtContributor>();
                services.AddScoped<ItDocCreateExtStorage>();
            });

        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        var dateUtc = new DateTime(2026, 01, 10, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var act = () => drafts.CreateDraftAsync(
            typeCode: TypeCode,
            number: "N-1",
            dateUtc: dateUtc,
            manageTransaction: false,
            ct: CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("This operation requires an active transaction.");
    }

    [Fact]
    public async Task CreateDraftAsync_ManageTransactionFalse_WhenOuterTransactionRollsBack_DoesNotPersistRegistry_TypedOrAudit()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItDocCreateExtContributor>();
                services.AddScoped<ItDocCreateExtStorage>();
            });

        var dateUtc = new DateTime(2026, 01, 10, 0, 0, 0, DateTimeKind.Utc);

        Guid id;

        // Act: create in external transaction mode and then rollback.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync();
            try
            {
                id = await drafts.CreateDraftAsync(TypeCode, number: "N-1", dateUtc: dateUtc, manageTransaction: false, ct: CancellationToken.None);

                var docCountInTx = await uow.Connection.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        "SELECT COUNT(*) FROM documents WHERE id = @id;",
                        new { id },
                        uow.Transaction,
                        cancellationToken: CancellationToken.None));

                docCountInTx.Should().Be(1);

                var typedCountInTx = await uow.Connection.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        $"SELECT COUNT(*) FROM {TypedTable} WHERE document_id = @id;",
                        new { id },
                        uow.Transaction,
                        cancellationToken: CancellationToken.None));

                typedCountInTx.Should().Be(1);
            }
            finally
            {
                await uow.RollbackAsync();
            }
        }

        // Assert: nothing persisted.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var docCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM documents WHERE id = @id;",
                new { id });

            docCount.Should().Be(0);

            var typedCount = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM {TypedTable} WHERE document_id = @id;",
                new { id });

            typedCount.Should().Be(0);
        }

        // Assert: audit not written.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: id,
                    ActionCode: AuditActionCodes.DocumentCreateDraft,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().BeEmpty();
        }
    }


    [Fact]
    public async Task CreateDraftAsync_ManageTransactionFalse_WhenOuterTransactionCommits_PersistsRegistry_TypedAndAudit()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItDocCreateExtContributor>();
                services.AddScoped<ItDocCreateExtStorage>();
            });

        var dateUtc = new DateTime(2026, 01, 10, 0, 0, 0, DateTimeKind.Utc);

        Guid id;

        // Act: create in external transaction mode and commit.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync();
            try
            {
                id = await drafts.CreateDraftAsync(TypeCode, number: "N-1", dateUtc: dateUtc, manageTransaction: false, ct: CancellationToken.None);
                await uow.CommitAsync();
            }
            catch
            {
                await uow.RollbackAsync();
                throw;
            }
        }

        // Assert: registry + typed persisted.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var docCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM documents WHERE id = @id;",
                new { id });

            docCount.Should().Be(1);

            var typedCount = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM {TypedTable} WHERE document_id = @id;",
                new { id });

            typedCount.Should().Be(1);
        }

        // Assert: audit written.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: id,
                    ActionCode: AuditActionCodes.DocumentCreateDraft,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
        }
    }

    private static async Task EnsureTypedTableExistsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // FK RESTRICT/NO ACTION to ensure the delete flow is correct for related tests.
        var ddl = $"""
CREATE TABLE IF NOT EXISTS {TypedTable} (
    document_id UUID PRIMARY KEY REFERENCES documents(id) ON DELETE RESTRICT,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
""";

        await conn.ExecuteAsync(ddl);
    }

    private sealed class ItDocCreateExtContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(TypeCode, b => b
                .Metadata(new DocumentTypeMetadata(
                    TypeCode,
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Doc Create Ext"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .TypedStorage<ItDocCreateExtStorage>());
        }
    }

    private sealed class ItDocCreateExtStorage(IUnitOfWork uow) : IDocumentTypeStorage
    {
        public string TypeCode => DocumentDraftService_CreateDraft_ExternalTransaction_Rollback_P0Tests.TypeCode;

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
