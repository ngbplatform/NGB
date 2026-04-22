using Dapper;
using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P0: DB-level constraints must protect the audit log even if application validation is bypassed.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseGuards_PlatformAuditLog_DbConstraints_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime T0 = new(2026, 1, 19, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PlatformAuditEvents_ActionCode_CheckConstraints_Enforced()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var emptyAct = async () =>
        {
            await conn.ExecuteAsync(
                """
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
                    @Id,
                    1,
                    @EntityId,
                    '',
                    NULL,
                    @T0,
                    NULL,
                    NULL
                );
                """,
                new { Id = Guid.CreateVersion7(), EntityId = Guid.CreateVersion7(), T0 });
        };

        var emptyEx = await emptyAct.Should().ThrowAsync<PostgresException>();
        emptyEx.Which.SqlState.Should().Be("23514");
        emptyEx.Which.ConstraintName.Should().Be("ck_platform_audit_events_action_code_nonempty");

        var tooLong = new string('a', 201);

        var tooLongAct = async () =>
        {
            await conn.ExecuteAsync(
                """
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
                    @Id,
                    1,
                    @EntityId,
                    @ActionCode,
                    NULL,
                    @T0,
                    NULL,
                    NULL
                );
                """,
                new { Id = Guid.CreateVersion7(), EntityId = Guid.CreateVersion7(), ActionCode = tooLong, T0 });
        };

        var tooLongEx = await tooLongAct.Should().ThrowAsync<PostgresException>();
        tooLongEx.Which.SqlState.Should().Be("23514");
        tooLongEx.Which.ConstraintName.Should().Be("ck_platform_audit_events_action_code_maxlen");
    }

    [Fact]
    public async Task PlatformAuditEvents_ActorUserId_ForeignKey_Enforced()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
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
                    @ActorUserId,
                    @T0,
                    NULL,
                    NULL
                );
                """,
                new
                {
                    EventId = Guid.CreateVersion7(),
                    EntityId = Guid.CreateVersion7(),
                    ActorUserId = Guid.CreateVersion7(),
                    T0
                });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23503");
        ex.Which.ConstraintName.Should().Be("fk_platform_audit_events_actor_user");
    }

    [Fact]
    public async Task PlatformAuditEventChanges_CheckConstraints_And_UniqueOrdinal_Enforced()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var eventId = Guid.CreateVersion7();

        await conn.ExecuteAsync(
            """
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
                NULL,
                @T0,
                NULL,
                NULL
            );
            """,
            new { EventId = eventId, EntityId = Guid.CreateVersion7(), T0 });

        // ordinal must be > 0
        var badOrdinalAct = async () =>
        {
            await conn.ExecuteAsync(
                """
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
                    0,
                    'status',
                    NULL,
                    NULL
                );
                """,
                new { ChangeId = Guid.CreateVersion7(), EventId = eventId });
        };

        var badOrdinalEx = await badOrdinalAct.Should().ThrowAsync<PostgresException>();
        badOrdinalEx.Which.SqlState.Should().Be("23514");
        badOrdinalEx.Which.ConstraintName.Should().Be("ck_platform_audit_event_changes_ordinal_positive");

        // field_path max length
        var tooLongPath = new string('x', 401);

        var badFieldPathAct = async () =>
        {
            await conn.ExecuteAsync(
                """
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
                    @FieldPath,
                    NULL,
                    NULL
                );
                """,
                new { ChangeId = Guid.CreateVersion7(), EventId = eventId, FieldPath = tooLongPath });
        };

        var badFieldPathEx = await badFieldPathAct.Should().ThrowAsync<PostgresException>();
        badFieldPathEx.Which.SqlState.Should().Be("23514");
        badFieldPathEx.Which.ConstraintName.Should().Be("ck_platform_audit_event_changes_field_path_maxlen");

        // unique (event_id, ordinal)
        await conn.ExecuteAsync(
            """
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
            new { ChangeId = Guid.CreateVersion7(), EventId = eventId });

        var dupOrdinalAct = async () =>
        {
            await conn.ExecuteAsync(
                """
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
                    'status2',
                    NULL,
                    NULL
                );
                """,
                new { ChangeId = Guid.CreateVersion7(), EventId = eventId });
        };

        var dupEx = await dupOrdinalAct.Should().ThrowAsync<PostgresException>();
        dupEx.Which.SqlState.Should().Be("23505");
        dupEx.Which.ConstraintName.Should().Be("ux_platform_audit_event_changes_event_ordinal");
    }

    [Fact]
    public async Task PlatformAuditEventChanges_EventForeignKey_Enforced()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
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
                    NULL,
                    NULL
                );
                """,
                new { ChangeId = Guid.CreateVersion7(), EventId = Guid.CreateVersion7() });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23503");
        ex.Which.ConstraintName.Should().Be("fk_platform_audit_event_changes_event");
    }
}
