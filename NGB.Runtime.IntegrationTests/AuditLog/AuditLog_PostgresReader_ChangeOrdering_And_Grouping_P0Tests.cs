using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_PostgresReader_ChangeOrdering_And_Grouping_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Reader_ReturnsChangesInOrdinalOrder_EvenIfInsertedOutOfOrder()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var auditEventId = Guid.CreateVersion7();
        var entityId = Guid.CreateVersion7();
        var occurredAtUtc = new DateTime(2026, 1, 20, 10, 0, 0, DateTimeKind.Utc);

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            const string insertEventSql = """
                                       INSERT INTO platform_audit_events
                                       (audit_event_id, entity_kind, entity_id, action_code, actor_user_id, occurred_at_utc, correlation_id, metadata)
                                       VALUES
                                       (@Id, @Kind, @EntityId, @Code, NULL, @At, NULL, NULL);
                                       """;

            await conn.ExecuteAsync(
                insertEventSql,
                new
                {
                    Id = auditEventId,
                    Kind = (short)AuditEntityKind.Document,
                    EntityId = entityId,
                    Code = "test.ordering",
                    At = occurredAtUtc
                });

            // Insert changes out-of-order: ordinal 2 first, then ordinal 1.
            // Reader must return them ordered by ordinal ASC.
            const string insertChangeSql = """
                                           INSERT INTO platform_audit_event_changes
                                           (audit_change_id, audit_event_id, ordinal, field_path, old_value_jsonb, new_value_jsonb)
                                           VALUES
                                           (@ChangeId, @EventId, @Ordinal, @FieldPath, @Old::jsonb, @New::jsonb);
                                           """;

            await conn.ExecuteAsync(
                insertChangeSql,
                new
                {
                    ChangeId = Guid.CreateVersion7(),
                    EventId = auditEventId,
                    Ordinal = 2,
                    FieldPath = "b",
                    Old = "\"old-b\"",
                    New = "\"new-b\""
                });

            await conn.ExecuteAsync(
                insertChangeSql,
                new
                {
                    ChangeId = Guid.CreateVersion7(),
                    EventId = auditEventId,
                    Ordinal = 1,
                    FieldPath = "a",
                    Old = "\"old-a\"",
                    New = "\"new-a\""
                });
        }

        // Act
        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var events = await reader.QueryAsync(
            new AuditLogQuery(
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId,
                ActionCode: "test.ordering",
                Limit: 20,
                Offset: 0),
            CancellationToken.None);

        // Assert
        events.Should().ContainSingle();
        var loaded = events.Single();

        loaded.Changes.Should().HaveCount(2);
        loaded.Changes[0].FieldPath.Should().Be("a");
        loaded.Changes[1].FieldPath.Should().Be("b");
    }

    [Fact]
    public async Task Reader_GroupsChangesPerEvent_AndPreservesEventOrdering()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var entityId = Guid.CreateVersion7();
        var olderEventId = Guid.CreateVersion7();
        var newerEventId = Guid.CreateVersion7();

        var olderAt = new DateTime(2026, 1, 19, 9, 0, 0, DateTimeKind.Utc);
        var newerAt = new DateTime(2026, 1, 19, 10, 0, 0, DateTimeKind.Utc);

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            const string insertEventSql = """
                                       INSERT INTO platform_audit_events
                                       (audit_event_id, entity_kind, entity_id, action_code, actor_user_id, occurred_at_utc, correlation_id, metadata)
                                       VALUES
                                       (@Id, @Kind, @EntityId, @Code, NULL, @At, NULL, NULL);
                                       """;

            // Older
            await conn.ExecuteAsync(
                insertEventSql,
                new
                {
                    Id = olderEventId,
                    Kind = (short)AuditEntityKind.Document,
                    EntityId = entityId,
                    Code = "test.group",
                    At = olderAt
                });

            // Newer
            await conn.ExecuteAsync(
                insertEventSql,
                new
                {
                    Id = newerEventId,
                    Kind = (short)AuditEntityKind.Document,
                    EntityId = entityId,
                    Code = "test.group",
                    At = newerAt
                });

            const string insertChangeSql = """
                                           INSERT INTO platform_audit_event_changes
                                           (audit_change_id, audit_event_id, ordinal, field_path, old_value_jsonb, new_value_jsonb)
                                           VALUES
                                           (@ChangeId, @EventId, @Ordinal, @FieldPath, @Old::jsonb, @New::jsonb);
                                           """;

            // Changes for older event
            await conn.ExecuteAsync(insertChangeSql, new { ChangeId = Guid.CreateVersion7(), EventId = olderEventId, Ordinal = 1, FieldPath = "old.1", Old = "0", New = "1" });
            await conn.ExecuteAsync(insertChangeSql, new { ChangeId = Guid.CreateVersion7(), EventId = olderEventId, Ordinal = 2, FieldPath = "old.2", Old = "1", New = "2" });

            // Changes for newer event
            await conn.ExecuteAsync(insertChangeSql, new { ChangeId = Guid.CreateVersion7(), EventId = newerEventId, Ordinal = 1, FieldPath = "new.1", Old = "10", New = "11" });
            await conn.ExecuteAsync(insertChangeSql, new { ChangeId = Guid.CreateVersion7(), EventId = newerEventId, Ordinal = 2, FieldPath = "new.2", Old = "11", New = "12" });
        }

        // Act
        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var events = await reader.QueryAsync(
            new AuditLogQuery(
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId,
                ActionCode: "test.group",
                Limit: 20,
                Offset: 0),
            CancellationToken.None);

        // Assert
        events.Should().HaveCount(2);

        // Ordered by occurred_at_utc DESC.
        events[0].AuditEventId.Should().Be(newerEventId);
        events[1].AuditEventId.Should().Be(olderEventId);

        events[0].Changes.Select(c => c.FieldPath).Should().Equal("new.1", "new.2");
        events[1].Changes.Select(c => c.FieldPath).Should().Equal("old.1", "old.2");
    }

    [Fact]
    public async Task AuditLogService_WriteAsync_WithNullChanges_WritesEventWithoutChangeRows()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                // Make actor explicit so we cover actor_user_id fill-path.
                services.AddScoped<ICurrentActorContext>(_ => new FixedCurrentActorContext(
                    new ActorIdentity(
                        AuthSubject: "kc|audit-null-changes",
                        Email: "null.changes@example.com",
                        DisplayName: "Null Changes",
                        IsActive: true)));
            });

        var entityId = Guid.CreateVersion7();

        Guid writtenEventId;

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            await audit.WriteAsync(
                entityKind: AuditEntityKind.Document,
                entityId: entityId,
                actionCode: "test.null_changes",
                changes: null,
                metadata: null,
                correlationId: null,
                ct: CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        // Assert (read back)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    ActionCode: "test.null_changes",
                    Limit: 20,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            var loaded = events.Single();
            writtenEventId = loaded.AuditEventId;

            loaded.Changes.Should().BeEmpty();
        }

        // Assert at DB-level: still no change rows for this event.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var changeCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_audit_event_changes WHERE audit_event_id = @id;",
                new { id = writtenEventId });

            changeCount.Should().Be(0);
        }
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}
