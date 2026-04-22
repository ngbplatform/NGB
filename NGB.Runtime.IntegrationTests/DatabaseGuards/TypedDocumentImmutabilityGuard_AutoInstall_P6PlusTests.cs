using Dapper;
using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

[Collection(PostgresCollection.Name)]
public sealed class TypedDocumentImmutabilityGuard_AutoInstall_P6PlusTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Installer_CreatesTrigger_ForNewTypedDocumentTable_WithDocumentId_AndEnforcesImmutability()
    {
        var cs = Fixture.ConnectionString;
        var tableName = "doc_test_typed_immutability";
        var docId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        try
        {
            // Create a brand new typed-doc table after bootstrap. The platform must be able to protect it
            // via a reusable trigger installed by convention (document_id).
            await conn.ExecuteAsync($"""
                                    CREATE TABLE IF NOT EXISTS {tableName} (
                                        document_id uuid NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
                                        note        text NULL
                                    );
                                    """);

            // Re-run installer explicitly (this simulates what happens on a next bootstrap).
            await conn.ExecuteAsync("SELECT ngb_install_typed_document_immutability_guards();");

            var triggerExists = await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM pg_trigger t
                JOIN pg_class cl ON cl.oid = t.tgrelid
                WHERE cl.relname = @table AND t.tgname = 'trg_posted_immutable';
                """,
                new { table = tableName });

            triggerExists.Should().BeGreaterThan(0);

            // Insert a draft document.
            await conn.ExecuteAsync(
                """
                INSERT INTO documents (id, type_code, number, date_utc, status, posted_at_utc, marked_for_deletion_at_utc, created_at_utc, updated_at_utc)
                VALUES (@id, @type, NULL, @dateUtc, 1, NULL, NULL, @created, @updated);
                """,
                new
                {
                    id = docId,
                    type = "test_typed_immutability",
                    dateUtc = now,
                    created = now,
                    updated = now
                });

            // Draft document: typed storage mutations must be allowed.
            await conn.ExecuteAsync($"INSERT INTO {tableName} (document_id, note) VALUES (@id, @note);", new { id = docId, note = "draft" });

            // Post the document.
            await conn.ExecuteAsync(
                """
                UPDATE documents
                SET status = 2,
                    posted_at_utc = @posted,
                    updated_at_utc = @updated
                WHERE id = @id;
                """,
                new { id = docId, posted = now.AddMinutes(1), updated = now.AddMinutes(1) });

            // Posted document: ANY mutation of typed storages must be blocked.
            var actInsert = async () => await conn.ExecuteAsync(
                $"INSERT INTO {tableName} (document_id, note) VALUES (@id, @note);",
                new { id = docId, note = "posted" });

            var actUpdate = async () => await conn.ExecuteAsync(
                $"UPDATE {tableName} SET note = @note WHERE document_id = @id;",
                new { id = docId, note = "mutated" });

            var actDelete = async () => await conn.ExecuteAsync(
                $"DELETE FROM {tableName} WHERE document_id = @id;",
                new { id = docId });

            (await actInsert.Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be("55000");

            (await actUpdate.Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be("55000");

            (await actDelete.Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be("55000");
        }
        finally
        {
            // Never leak schema changes across integration tests.
            await conn.ExecuteAsync($"DROP TABLE IF EXISTS {tableName};");
        }
    }

    [Fact]
    public async Task Bootstrap_Ensures_AllTypedDocumentTables_AreProtected_ByReusableTrigger()
    {
        var cs = Fixture.ConnectionString;
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        var missingTables = (await conn.QueryAsync<string>(
            """
            SELECT c.table_name
            FROM information_schema.columns c
            WHERE c.table_schema = 'public'
              AND c.column_name = 'document_id'
              AND c.table_name LIKE 'doc\_%' ESCAPE '\'
              AND NOT EXISTS (
                SELECT 1
                FROM pg_trigger t
                JOIN pg_class cl ON cl.oid = t.tgrelid
                JOIN pg_namespace ns ON ns.oid = cl.relnamespace
                WHERE t.tgname = 'trg_posted_immutable'
                  AND ns.nspname = c.table_schema
                  AND cl.relname = c.table_name
              )
            ORDER BY c.table_name;
            """))
            .ToArray();

        missingTables.Should().BeEmpty("every typed document table must be guarded by trg_posted_immutable");
    }
}
