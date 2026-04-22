using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_Reader_CombinedFilters_WithCursor_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task QueryAsync_WithEntityAndActorAndActionFilters_SupportsCursorPaging()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var entityId1 = DeterministicGuid.Create("audit|combined|entity|1");
        var entityId2 = DeterministicGuid.Create("audit|combined|entity|2");
        var baseTime = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        Guid user1;
        Guid user2;

        // Seed events
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            user1 = await users.UpsertAsync(
                authSubject: "kc-subject-u1",
                email: "u1@example.com",
                displayName: "User 1",
                isActive: true,
                CancellationToken.None);

            user2 = await users.UpsertAsync(
                authSubject: "kc-subject-u2",
                email: "u2@example.com",
                displayName: "User 2",
                isActive: true,
                CancellationToken.None);

            // Matching (entityId1, user1, action=document.post): 3 events.
            await writer.WriteAsync(new AuditEvent(
                AuditEventId: DeterministicGuid.Create("audit|combined|e|m4"),
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId1,
                ActionCode: "document.post",
                ActorUserId: user1,
                OccurredAtUtc: baseTime.AddMinutes(4),
                CorrelationId: null,
                MetadataJson: null,
                Changes: Array.Empty<AuditFieldChange>()),
                CancellationToken.None);

            await writer.WriteAsync(new AuditEvent(
                AuditEventId: DeterministicGuid.Create("audit|combined|e|m3"),
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId1,
                ActionCode: "document.post",
                ActorUserId: user1,
                OccurredAtUtc: baseTime.AddMinutes(3),
                CorrelationId: null,
                MetadataJson: null,
                Changes: Array.Empty<AuditFieldChange>()),
                CancellationToken.None);

            await writer.WriteAsync(new AuditEvent(
                AuditEventId: DeterministicGuid.Create("audit|combined|e|m0"),
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId1,
                ActionCode: "document.post",
                ActorUserId: user1,
                OccurredAtUtc: baseTime.AddMinutes(0),
                CorrelationId: null,
                MetadataJson: null,
                Changes: Array.Empty<AuditFieldChange>()),
                CancellationToken.None);

            // Noise: different actor
            await writer.WriteAsync(new AuditEvent(
                AuditEventId: DeterministicGuid.Create("audit|combined|e|noise-actor"),
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId1,
                ActionCode: "document.post",
                ActorUserId: user2,
                OccurredAtUtc: baseTime.AddMinutes(2),
                CorrelationId: null,
                MetadataJson: null,
                Changes: Array.Empty<AuditFieldChange>()),
                CancellationToken.None);

            // Noise: different action
            await writer.WriteAsync(new AuditEvent(
                AuditEventId: DeterministicGuid.Create("audit|combined|e|noise-action"),
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId1,
                ActionCode: "document.unpost",
                ActorUserId: user1,
                OccurredAtUtc: baseTime.AddMinutes(1),
                CorrelationId: null,
                MetadataJson: null,
                Changes: Array.Empty<AuditFieldChange>()),
                CancellationToken.None);

            // Noise: different entity
            await writer.WriteAsync(new AuditEvent(
                AuditEventId: DeterministicGuid.Create("audit|combined|e|noise-entity"),
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId2,
                ActionCode: "document.post",
                ActorUserId: user1,
                OccurredAtUtc: baseTime.AddMinutes(5),
                CorrelationId: null,
                MetadataJson: null,
                Changes: Array.Empty<AuditFieldChange>()),
                CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var page1 = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId1,
                    ActorUserId: user1,
                    ActionCode: "  document.post  ",
                    Limit: 2,
                    Offset: 0),
                CancellationToken.None);

            page1.Should().HaveCount(2);
            page1[0].OccurredAtUtc.Should().Be(baseTime.AddMinutes(4));
            page1[1].OccurredAtUtc.Should().Be(baseTime.AddMinutes(3));

            var cursor = page1[^1];

            var page2 = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId1,
                    ActorUserId: user1,
                    ActionCode: "document.post",
                    AfterOccurredAtUtc: cursor.OccurredAtUtc,
                    AfterAuditEventId: cursor.AuditEventId,
                    Limit: 10,
                    Offset: 0),
                CancellationToken.None);

            page2.Should().ContainSingle();
            page2[0].OccurredAtUtc.Should().Be(baseTime.AddMinutes(0));

            // No overlap.
            page1.Select(x => x.AuditEventId).Intersect(page2.Select(x => x.AuditEventId)).Should().BeEmpty();

            var cursor2 = page2[^1];

            var page3 = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId1,
                    ActorUserId: user1,
                    ActionCode: "document.post",
                    AfterOccurredAtUtc: cursor2.OccurredAtUtc,
                    AfterAuditEventId: cursor2.AuditEventId,
                    Limit: 10,
                    Offset: 0),
                CancellationToken.None);

            page3.Should().BeEmpty();
        }
    }
}
