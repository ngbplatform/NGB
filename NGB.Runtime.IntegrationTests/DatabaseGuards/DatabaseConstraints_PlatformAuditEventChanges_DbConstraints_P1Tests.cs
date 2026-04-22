using Dapper;
using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P1: platform_audit_event_changes must be protected by hard DB constraints.
/// These tests validate enforcement at the database level (CHECK + FK) using direct inserts.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseConstraints_PlatformAuditEventChanges_DbConstraints_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CheckConstraints_Forbid_NonPositiveOrdinal_And_EmptyOrTooLongFieldPath()
    {
        await Fixture.ResetDatabaseAsync();
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var eventId = Guid.CreateVersion7();
        await conn.ExecuteAsync(
            "INSERT INTO platform_audit_events(audit_event_id, entity_kind, entity_id, action_code) VALUES (@Id, 1, @EntityId, 'document.post');",
            new { Id = eventId, EntityId = Guid.CreateVersion7() });

        var id1 = Guid.CreateVersion7();
        var actOrdinal = async () =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO platform_audit_event_changes(audit_change_id, audit_event_id, ordinal, field_path) VALUES (@Id, @EventId, 0, 'number');",
                new { Id = id1, EventId = eventId });
        };

        var exOrdinal = await actOrdinal.Should().ThrowAsync<PostgresException>();
        exOrdinal.Which.SqlState.Should().Be("23514");
        exOrdinal.Which.ConstraintName.Should().Be("ck_platform_audit_event_changes_ordinal_positive");

        var id2 = Guid.CreateVersion7();
        var actPathEmpty = async () =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO platform_audit_event_changes(audit_change_id, audit_event_id, ordinal, field_path) VALUES (@Id, @EventId, 1, '   ');",
                new { Id = id2, EventId = eventId });
        };

        var exPathEmpty = await actPathEmpty.Should().ThrowAsync<PostgresException>();
        exPathEmpty.Which.SqlState.Should().Be("23514");
        exPathEmpty.Which.ConstraintName.Should().Be("ck_platform_audit_event_changes_field_path_nonempty");

        var id3 = Guid.CreateVersion7();
        var tooLongPath = new string('x', 401);

        var actPathTooLong = async () =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO platform_audit_event_changes(audit_change_id, audit_event_id, ordinal, field_path) VALUES (@Id, @EventId, 1, @Path);",
                new { Id = id3, EventId = eventId, Path = tooLongPath });
        };

        var exPathTooLong = await actPathTooLong.Should().ThrowAsync<PostgresException>();
        exPathTooLong.Which.SqlState.Should().Be("23514");
        exPathTooLong.Which.ConstraintName.Should().Be("ck_platform_audit_event_changes_field_path_maxlen");
    }

    [Fact]
    public async Task ForeignKey_Forbids_UnknownAuditEventId()
    {
        await Fixture.ResetDatabaseAsync();
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var id = Guid.CreateVersion7();

        var act = async () =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO platform_audit_event_changes(audit_change_id, audit_event_id, ordinal, field_path) VALUES (@Id, @EventId, 1, 'number');",
                new { Id = id, EventId = Guid.CreateVersion7() });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23503");
        ex.Which.ConstraintName.Should().Be("fk_platform_audit_event_changes_event");
    }

    [Fact]
    public async Task ValidRow_IsAccepted()
    {
        await Fixture.ResetDatabaseAsync();
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var eventId = Guid.CreateVersion7();
        await conn.ExecuteAsync(
            "INSERT INTO platform_audit_events(audit_event_id, entity_kind, entity_id, action_code) VALUES (@Id, 1, @EntityId, 'document.post');",
            new { Id = eventId, EntityId = Guid.CreateVersion7() });

        var changeId = Guid.CreateVersion7();
        await conn.ExecuteAsync(
            "INSERT INTO platform_audit_event_changes(audit_change_id, audit_event_id, ordinal, field_path) VALUES (@Id, @EventId, 1, 'number');",
            new { Id = changeId, EventId = eventId });

        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_event_changes WHERE audit_change_id = @Id;",
            new { Id = changeId });

        count.Should().Be(1);
    }
}
