using Dapper;
using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P1: platform_audit_events must be protected by hard DB constraints.
/// These tests validate enforcement at the database level (CHECK + FK),
/// independent of runtime validators.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseConstraints_PlatformAuditEvents_DbConstraints_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CheckConstraints_Forbid_EmptyAndTooLongActionCode()
    {
        await Fixture.ResetDatabaseAsync();
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var id1 = Guid.CreateVersion7();

        var actEmpty = async () =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO platform_audit_events(audit_event_id, entity_kind, entity_id, action_code) VALUES (@Id, 1, @EntityId, '   ');",
                new { Id = id1, EntityId = Guid.CreateVersion7() });
        };

        var exEmpty = await actEmpty.Should().ThrowAsync<PostgresException>();
        exEmpty.Which.SqlState.Should().Be("23514");
        exEmpty.Which.ConstraintName.Should().Be("ck_platform_audit_events_action_code_nonempty");

        var id2 = Guid.CreateVersion7();
        var longCode = new string('A', 201);

        var actTooLong = async () =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO platform_audit_events(audit_event_id, entity_kind, entity_id, action_code) VALUES (@Id, 1, @EntityId, @ActionCode);",
                new { Id = id2, EntityId = Guid.CreateVersion7(), ActionCode = longCode });
        };

        var exTooLong = await actTooLong.Should().ThrowAsync<PostgresException>();
        exTooLong.Which.SqlState.Should().Be("23514");
        exTooLong.Which.ConstraintName.Should().Be("ck_platform_audit_events_action_code_maxlen");
    }

    [Fact]
    public async Task ForeignKey_Forbids_UnknownActorUserId()
    {
        await Fixture.ResetDatabaseAsync();
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var id = Guid.CreateVersion7();
        var unknownUserId = Guid.CreateVersion7();

        var act = async () =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO platform_audit_events(audit_event_id, entity_kind, entity_id, action_code, actor_user_id) VALUES (@Id, 1, @EntityId, 'document.post', @UserId);",
                new { Id = id, EntityId = Guid.CreateVersion7(), UserId = unknownUserId });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23503");
        ex.Which.ConstraintName.Should().Be("fk_platform_audit_events_actor_user");
    }

    [Fact]
    public async Task ValidRow_IsAccepted_WithOrWithoutActor()
    {
        await Fixture.ResetDatabaseAsync();
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        // Without actor
        var eventId1 = Guid.CreateVersion7();
        await conn.ExecuteAsync(
            "INSERT INTO platform_audit_events(audit_event_id, entity_kind, entity_id, action_code) VALUES (@Id, 1, @EntityId, 'document.create_draft');",
            new { Id = eventId1, EntityId = Guid.CreateVersion7() });

        // With actor
        var userId = Guid.CreateVersion7();
        await conn.ExecuteAsync(
            "INSERT INTO platform_users(user_id, auth_subject, email, display_name) VALUES (@Id, 'sub-1', 'sub1@example.com', 'Sub One');",
            new { Id = userId });

        var eventId2 = Guid.CreateVersion7();
        await conn.ExecuteAsync(
            "INSERT INTO platform_audit_events(audit_event_id, entity_kind, entity_id, action_code, actor_user_id) VALUES (@Id, 1, @EntityId, 'document.post', @UserId);",
            new { Id = eventId2, EntityId = Guid.CreateVersion7(), UserId = userId });

        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_events WHERE audit_event_id = ANY(@Ids);",
            new { Ids = new[] { eventId1, eventId2 } });

        count.Should().Be(2);
    }
}
