using Dapper;
using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P1: platform audit tables are append-only.
///
/// Defense in depth: regardless of application logic, the DB must block UPDATE/DELETE
/// on platform_audit_events and platform_audit_event_changes.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PlatformAuditAppendOnlyGuards_Enforced_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string InsertEventSql = @"
INSERT INTO platform_audit_events
    (audit_event_id, entity_kind, entity_id, action_code, actor_user_id, occurred_at_utc, correlation_id, metadata)
VALUES
    (@id, @kind, @entity_id, @action, NULL, @occurred_at_utc, NULL, NULL);";

    private const string InsertChangeSql = @"
INSERT INTO platform_audit_event_changes
    (audit_change_id, audit_event_id, ordinal, field_path, old_value_jsonb, new_value_jsonb)
VALUES
    (@id, @event_id, 1, 'x', '{}'::jsonb, '{}'::jsonb);";

    [Fact]
    public async Task UpdateAndDeleteAreForbidden_OnAuditEvents_And_AuditEventChanges()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        var occurredAtUtc = new DateTime(2026, 1, 8, 12, 0, 0, DateTimeKind.Utc);

        var auditEventId = Guid.CreateVersion7();
        await conn.ExecuteAsync(InsertEventSql, new
        {
            id = auditEventId,
            kind = (short)1,
            entity_id = Guid.CreateVersion7(),
            action = "it.append_only",
            occurred_at_utc = occurredAtUtc
        });

        var auditChangeId = Guid.CreateVersion7();
        await conn.ExecuteAsync(InsertChangeSql, new { id = auditChangeId, event_id = auditEventId });

        await AssertAppendOnlyGuardAsync(
            () => conn.ExecuteAsync(
                "UPDATE platform_audit_events SET metadata = jsonb_build_object('k','v') WHERE audit_event_id = @id;",
                new { id = auditEventId }),
            expectedTableName: "platform_audit_events");

        await AssertAppendOnlyGuardAsync(
            () => conn.ExecuteAsync(
                "DELETE FROM platform_audit_events WHERE audit_event_id = @id;",
                new { id = auditEventId }),
            expectedTableName: "platform_audit_events");

        await AssertAppendOnlyGuardAsync(
            () => conn.ExecuteAsync(
                "UPDATE platform_audit_event_changes SET ordinal = ordinal + 1 WHERE audit_change_id = @id;",
                new { id = auditChangeId }),
            expectedTableName: "platform_audit_event_changes");

        await AssertAppendOnlyGuardAsync(
            () => conn.ExecuteAsync(
                "DELETE FROM platform_audit_event_changes WHERE audit_change_id = @id;",
                new { id = auditChangeId }),
            expectedTableName: "platform_audit_event_changes");
    }

    private static async Task AssertAppendOnlyGuardAsync(Func<Task> act, string expectedTableName)
    {
        var ex = await FluentActions.Invoking(act).Should().ThrowAsync<PostgresException>();

        ex.Which.SqlState.Should().Be("55000");
        ex.Which.Message.Should().Contain("Append-only table cannot be mutated");
        ex.Which.Message.Should().Contain(expectedTableName);
    }
}
