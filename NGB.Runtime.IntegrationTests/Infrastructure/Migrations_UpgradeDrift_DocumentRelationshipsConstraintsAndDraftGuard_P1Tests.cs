using Dapper;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P1 drift-repair: document_relationships is protected by hard DB constraints and a draft-guard trigger.
/// Our migration runner is CREATE/ALTER IF NOT EXISTS style, so dropping these objects must be recoverable
/// by re-applying platform migrations.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class Migrations_UpgradeDrift_DocumentRelationshipsConstraintsAndDraftGuard_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ApplyPlatformMigrations_RecreatesDroppedDocumentRelationshipsConstraints_AndDraftGuardTrigger()
    {
        await Fixture.ResetDatabaseAsync();

        // Drop constraints. (They are re-added by DocumentRelationshipsMigration via conditional ALTER TABLE.)
        await ExecuteAsync(
            Fixture.ConnectionString,
            """
            ALTER TABLE public.document_relationships DROP CONSTRAINT IF EXISTS ck_document_relationships_code_trimmed;
            ALTER TABLE public.document_relationships DROP CONSTRAINT IF EXISTS ck_document_relationships_code_nonempty;
            ALTER TABLE public.document_relationships DROP CONSTRAINT IF EXISTS ck_document_relationships_code_len;
            ALTER TABLE public.document_relationships DROP CONSTRAINT IF EXISTS ck_document_relationships_not_self;

            ALTER TABLE public.document_relationships DROP CONSTRAINT IF EXISTS ux_document_relationships_triplet;

            ALTER TABLE public.document_relationships DROP CONSTRAINT IF EXISTS fk_document_relationships_from_document;
            ALTER TABLE public.document_relationships DROP CONSTRAINT IF EXISTS fk_document_relationships_to_document;
            """);

        // Drop trigger. (It is deterministically re-installed by DocumentRelationshipsDraftGuardMigration.)
        await ExecuteAsync(
            Fixture.ConnectionString,
            """
            DROP TRIGGER IF EXISTS trg_document_relationships_draft_guard ON public.document_relationships;
            """);

        // Sanity: objects are gone.
        (await ConstraintExistsAsync(Fixture.ConnectionString, "ck_document_relationships_code_trimmed")).Should().BeFalse();
        (await ConstraintExistsAsync(Fixture.ConnectionString, "ck_document_relationships_code_nonempty")).Should().BeFalse();
        (await ConstraintExistsAsync(Fixture.ConnectionString, "ck_document_relationships_code_len")).Should().BeFalse();
        (await ConstraintExistsAsync(Fixture.ConnectionString, "ck_document_relationships_not_self")).Should().BeFalse();
        (await ConstraintExistsAsync(Fixture.ConnectionString, "ux_document_relationships_triplet")).Should().BeFalse();
        (await ConstraintExistsAsync(Fixture.ConnectionString, "fk_document_relationships_from_document")).Should().BeFalse();
        (await ConstraintExistsAsync(Fixture.ConnectionString, "fk_document_relationships_to_document")).Should().BeFalse();
        (await TriggerExistsAsync(Fixture.ConnectionString, "trg_document_relationships_draft_guard")).Should().BeFalse();

        // Act: re-apply migrations.
        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        // Assert: everything is back.
        (await ConstraintExistsAsync(Fixture.ConnectionString, "ck_document_relationships_code_trimmed")).Should().BeTrue();
        (await ConstraintExistsAsync(Fixture.ConnectionString, "ck_document_relationships_code_nonempty")).Should().BeTrue();
        (await ConstraintExistsAsync(Fixture.ConnectionString, "ck_document_relationships_code_len")).Should().BeTrue();
        (await ConstraintExistsAsync(Fixture.ConnectionString, "ck_document_relationships_not_self")).Should().BeTrue();
        (await ConstraintExistsAsync(Fixture.ConnectionString, "ux_document_relationships_triplet")).Should().BeTrue();
        (await ConstraintExistsAsync(Fixture.ConnectionString, "fk_document_relationships_from_document")).Should().BeTrue();
        (await ConstraintExistsAsync(Fixture.ConnectionString, "fk_document_relationships_to_document")).Should().BeTrue();
        (await TriggerExistsAsync(Fixture.ConnectionString, "trg_document_relationships_draft_guard")).Should().BeTrue();
    }

    private static async Task ExecuteAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);
        await conn.ExecuteAsync(sql);
    }

    private static async Task<bool> ConstraintExistsAsync(string cs, string constraintName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        var exists = await conn.ExecuteScalarAsync<int>(
            """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM pg_constraint c
                WHERE c.conname = @name
            ) THEN 1 ELSE 0 END;
            """,
            new { name = constraintName });

        return exists == 1;
    }

    private static async Task<bool> TriggerExistsAsync(string cs, string triggerName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        var exists = await conn.ExecuteScalarAsync<int>(
            """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM pg_trigger t
                WHERE t.tgname = @name
                  AND NOT t.tgisinternal
            ) THEN 1 ELSE 0 END;
            """,
            new { name = triggerName });

        return exists == 1;
    }
}
