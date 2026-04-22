using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Catalogs;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;
using NGB.Definitions;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_Atomicity_Rollback_Catalog_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    // Use unique catalogCode/table names to avoid colliding with real module typed tables.
    private const string CatalogCode = "it_cat_aud_rb";
    private const string TypedTable = "cat_it_cat_aud_rb";

    private const string AuthSubjectCreate = "kc|audit-cat-create-rollback-test";
    private const string AuthSubjectDelete = "kc|audit-cat-delete-rollback-test";

    [Fact]
    public async Task CreateAsync_WhenAuditWriterThrowsAfterInsert_RollsBack_Catalog_TypedStorage_Audit_And_Actor()
    {
        // Arrange
        await EnsureTypedTableExistsAndEmptyAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>();
                services.AddScoped<ItCatalogTypeStorage>();
                services.AddScoped<ICatalogTypeStorage>(sp => sp.GetRequiredService<ItCatalogTypeStorage>());

                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: AuthSubjectCreate,
                        Email: "audit.cat.create.rollback@example.com",
                        DisplayName: "Audit Cat Create Rollback")));

                // Override the AuditEvent writer to throw AFTER it wrote the event + changes.
                // This simulates a failure happening late in the business transaction.
                services.AddScoped<PostgresAuditEventWriter>();
                services.AddScoped<IAuditEventWriter>(sp =>
                    new ThrowAfterWriteAuditEventWriter(sp.GetRequiredService<PostgresAuditEventWriter>()));
            });

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();

            var act = () => drafts.CreateAsync(
                catalogCode: CatalogCode,
                manageTransaction: true,
                ct: CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*simulated audit failure*");
        }

        // Assert
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var catCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM catalogs WHERE catalog_code = @c;",
            new { c = CatalogCode });

        catCount.Should().Be(0, "audit failure must rollback the catalog registry insert");

        var typedCount = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {TypedTable};");
        typedCount.Should().Be(0, "audit failure must rollback typed storage insert");

        var eventCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
        eventCount.Should().Be(0, "audit rows must not be committed if the transaction rolls back");

        var changeCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        changeCount.Should().Be(0, "audit change rows must not be committed if the transaction rolls back");

        var userCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = AuthSubjectCreate });

        userCount.Should().Be(0, "actor upsert must rollback together with audit event");
    }

    [Fact]
    public async Task MarkForDeletionAsync_ManageTransactionFalse_WhenAuditWriterThrowsAfterInsert_ExternalRollback_DoesNotMarkDeleted_AndKeepsTypedRow_AndDoesNotCommitAuditOrActor()
    {
        // Arrange
        await EnsureTypedTableExistsAndEmptyAsync(Fixture.ConnectionString);

        Guid catalogId;
        await using (var scope = IntegrationHostFactory.Create(
                         Fixture.ConnectionString,
                         services =>
                         {
                             services.AddScoped<ItCatalogTypeStorage>();
                             services.AddScoped<ICatalogTypeStorage>(sp => sp.GetRequiredService<ItCatalogTypeStorage>());
                             services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>();
                         }).Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            catalogId = await drafts.CreateAsync(CatalogCode, manageTransaction: true, ct: CancellationToken.None);
        }

        int baselineEvents;
        int baselineChanges;
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();
            baselineEvents = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
            baselineChanges = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        }

        using var failingHost = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>();
                services.AddScoped<ItCatalogTypeStorage>();
                services.AddScoped<ICatalogTypeStorage>(sp => sp.GetRequiredService<ItCatalogTypeStorage>());

                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: AuthSubjectDelete,
                        Email: "audit.cat.delete.rollback@example.com",
                        DisplayName: "Audit Cat Delete Rollback")));

                services.AddScoped<PostgresAuditEventWriter>();
                services.AddScoped<IAuditEventWriter>(sp =>
                    new ThrowAfterWriteAuditEventWriter(sp.GetRequiredService<PostgresAuditEventWriter>()));
            });

        // Act (external transaction mode)
        await using (var scope = failingHost.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync();
            try
            {
                var act = () => drafts.MarkForDeletionAsync(
                    catalogId: catalogId,
                    manageTransaction: false,
                    ct: CancellationToken.None);

                await act.Should().ThrowAsync<NotSupportedException>()
                    .WithMessage("*simulated audit failure*");
            }
            finally
            {
                await uow.RollbackAsync();
            }
        }

        // Assert
        await using var assertConn = new NpgsqlConnection(Fixture.ConnectionString);
        await assertConn.OpenAsync();

        var isDeleted = await assertConn.ExecuteScalarAsync<bool>(
            "SELECT is_deleted FROM catalogs WHERE id = @id;",
            new { id = catalogId });

        isDeleted.Should().BeFalse("external rollback must undo the mark-deleted registry update");

        var typedCount = await assertConn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {TypedTable} WHERE catalog_id = @id;",
            new { id = catalogId });

        typedCount.Should().Be(1, "external rollback must undo typed storage delete");

        var eventCount = await assertConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
        eventCount.Should().Be(baselineEvents, "no audit rows must be committed if the transaction rolls back");

        var changeCount = await assertConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        changeCount.Should().Be(baselineChanges, "no audit change rows must be committed if the transaction rolls back");

        var userCount = await assertConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = AuthSubjectDelete });

        userCount.Should().Be(0, "actor upsert must rollback together with audit event");
    }

    private static async Task EnsureTypedTableExistsAndEmptyAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var ddl = $"""
CREATE TABLE IF NOT EXISTS {TypedTable} (
    catalog_id UUID PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    name TEXT NOT NULL DEFAULT '',
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
""";
        await conn.ExecuteAsync(ddl);
        await conn.ExecuteAsync($"TRUNCATE TABLE {TypedTable};");
    }

    sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }

    sealed class ThrowAfterWriteAuditEventWriter(IAuditEventWriter inner) : IAuditEventWriter
    {
        public async Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            await inner.WriteAsync(auditEvent, ct);
            throw new NotSupportedException("simulated audit failure");
        }

        public async Task WriteBatchAsync(IReadOnlyList<AuditEvent> auditEvents, CancellationToken ct = default)
        {
            if (auditEvents is null)
                throw new ArgumentNullException(nameof(auditEvents));

            for (var i = 0; i < auditEvents.Count; i++)
                await WriteAsync(auditEvents[i], ct);
        }
    }

    sealed class ItCatalogTypeStorage(IUnitOfWork uow) : ICatalogTypeStorage
    {
        public string CatalogCode => AuditLog_Atomicity_Rollback_Catalog_P1Tests.CatalogCode;

        public async Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default)
        {
            uow.EnsureActiveTransaction();

            var sql =
                $"INSERT INTO {TypedTable} (catalog_id, name) VALUES (@catalogId, 'test') ON CONFLICT (catalog_id) DO NOTHING;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { catalogId },
                uow.Transaction,
                cancellationToken: ct));
        }

        public async Task DeleteAsync(Guid catalogId, CancellationToken ct = default)
        {
            uow.EnsureActiveTransaction();

            var sql = $"DELETE FROM {TypedTable} WHERE catalog_id = @catalogId;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { catalogId },
                uow.Transaction,
                cancellationToken: ct));
        }
    }
}
