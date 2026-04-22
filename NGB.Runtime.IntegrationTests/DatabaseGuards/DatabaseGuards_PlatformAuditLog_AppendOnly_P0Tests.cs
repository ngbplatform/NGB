using Dapper;
using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P0: Business audit log tables must be append-only at the database level.
/// This protects the audit trail even if a bug slips through the application layer.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseGuards_PlatformAuditLog_AppendOnly_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime T0 = new(2026, 1, 19, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PlatformAudit_Tables_Exist_AfterBootstrap()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var tables = await conn.QueryAsync<string>(
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_type = 'BASE TABLE'
            ORDER BY table_name;
            """);

        tables.Should().Contain(new[]
        {
            "platform_users",
            "platform_audit_events",
            "platform_audit_event_changes"
        });
    }

    [Fact]
    public async Task PlatformAuditEvents_And_Changes_AreAppendOnly_UpdateAndDeleteThrow()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var userId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var changeId = Guid.CreateVersion7();

        await conn.ExecuteAsync(
            """
            INSERT INTO platform_users(user_id, auth_subject, email, display_name, is_active, created_at_utc, updated_at_utc)
            VALUES (@UserId, @Sub, 'user@example.com', 'User', TRUE, @T0, @T0);

            INSERT INTO platform_audit_events(
                audit_event_id,
                entity_kind,
                entity_id,
                action_code,
                actor_user_id,
                occurred_at_utc,
                correlation_id,
                metadata
            ) VALUES (
                @EventId,
                1,
                @EntityId,
                'document.post',
                @UserId,
                @T0,
                NULL,
                NULL
            );

            INSERT INTO platform_audit_event_changes(
                audit_change_id,
                audit_event_id,
                ordinal,
                field_path,
                old_value_jsonb,
                new_value_jsonb
            ) VALUES (
                @ChangeId,
                @EventId,
                1,
                'status',
                '"Draft"'::jsonb,
                '"Posted"'::jsonb
            );
            """,
            new
            {
                UserId = userId,
                Sub = "kc-subject-1",
                EventId = eventId,
                ChangeId = changeId,
                EntityId = Guid.CreateVersion7(),
                T0
            });

        // UPDATE should be forbidden (append-only)
        var updateAct = async () =>
        {
            await conn.ExecuteAsync(
                "UPDATE platform_audit_events SET action_code='document.unpost' WHERE audit_event_id=@EventId;",
                new { EventId = eventId });
        };

        var updateEx = await updateAct.Should().ThrowAsync<PostgresException>();
        updateEx.Which.SqlState.Should().Be("55000");

        // DELETE should be forbidden (append-only)
        var deleteAct = async () =>
        {
            await conn.ExecuteAsync(
                "DELETE FROM platform_audit_event_changes WHERE audit_change_id=@ChangeId;",
                new { ChangeId = changeId });
        };

        var deleteEx = await deleteAct.Should().ThrowAsync<PostgresException>();
        deleteEx.Which.SqlState.Should().Be("55000");
    }

    [Fact]
    public async Task PlatformUsers_AuthSubject_IsRequired_ByDbConstraint()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO platform_users(user_id, auth_subject, email, display_name, is_active, created_at_utc, updated_at_utc)
                VALUES (@UserId, '', NULL, NULL, TRUE, @T0, @T0);
                """,
                new { UserId = Guid.CreateVersion7(), T0 });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_platform_users_auth_subject_nonempty");
    }
}
