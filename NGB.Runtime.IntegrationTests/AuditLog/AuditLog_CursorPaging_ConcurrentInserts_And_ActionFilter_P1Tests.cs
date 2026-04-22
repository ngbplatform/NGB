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
public sealed class AuditLog_CursorPaging_ConcurrentInserts_And_ActionFilter_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task QueryAsync_CursorPaging_WhenNewerEventsInsertedBetweenPages_DoesNotShiftOrDuplicateOlderPages()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var entityId = DeterministicGuid.Create("audit|entity|cursor|concurrent");
        var baseTime = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        // Seed 10 initial events at unique minutes 0..9.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            for (var i = 0; i < 10; i++)
            {
                var ev = new AuditEvent(
                    AuditEventId: DeterministicGuid.Create($"audit|event|concurrent|seed|{i:D2}"),
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    ActionCode: "document.post",
                    ActorUserId: null,
                    OccurredAtUtc: baseTime.AddMinutes(i),
                    CorrelationId: null,
                    MetadataJson: null,
                    Changes: Array.Empty<AuditFieldChange>());

                await writer.WriteAsync(ev, CancellationToken.None);
            }

            await uow.CommitAsync(CancellationToken.None);
        }

        IReadOnlyList<AuditEvent> page1;
        AuditEvent cursor;

        // Page 1 (before concurrent inserts).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            page1 = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    Limit: 4,
                    Offset: 0),
                CancellationToken.None);

            page1.Should().HaveCount(4);
            page1.Select(x => x.OccurredAtUtc).Should().Equal(
                baseTime.AddMinutes(9),
                baseTime.AddMinutes(8),
                baseTime.AddMinutes(7),
                baseTime.AddMinutes(6));

            cursor = page1[^1];
        }

        // Concurrent inserts: newer events for the SAME entity after we obtained the cursor.
        var newer1 = DeterministicGuid.Create("audit|event|concurrent|newer|01");
        var newer2 = DeterministicGuid.Create("audit|event|concurrent|newer|02");
        var sameTimeHigherId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            await writer.WriteAsync(
                new AuditEvent(
                    AuditEventId: newer1,
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    ActionCode: "document.post",
                    ActorUserId: null,
                    OccurredAtUtc: baseTime.AddMinutes(101),
                    CorrelationId: null,
                    MetadataJson: null,
                    Changes: Array.Empty<AuditFieldChange>()),
                CancellationToken.None);

            await writer.WriteAsync(
                new AuditEvent(
                    AuditEventId: newer2,
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    ActionCode: "document.post",
                    ActorUserId: null,
                    OccurredAtUtc: baseTime.AddMinutes(100),
                    CorrelationId: null,
                    MetadataJson: null,
                    Changes: Array.Empty<AuditFieldChange>()),
                CancellationToken.None);

            // Same timestamp as the cursor row, but a higher UUID. This event would appear before the cursor row
            // in a fresh query; it must not appear on the next cursor page.
            await writer.WriteAsync(
                new AuditEvent(
                    AuditEventId: sameTimeHigherId,
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    ActionCode: "document.post",
                    ActorUserId: null,
                    OccurredAtUtc: cursor.OccurredAtUtc,
                    CorrelationId: null,
                    MetadataJson: null,
                    Changes: Array.Empty<AuditFieldChange>()),
                CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        // Page 2 must continue from the cursor without being affected by the newer inserts.
        IReadOnlyList<AuditEvent> page2;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            page2 = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    AfterOccurredAtUtc: cursor.OccurredAtUtc,
                    AfterAuditEventId: cursor.AuditEventId,
                    Limit: 4,
                    Offset: 0),
                CancellationToken.None);

            page2.Should().HaveCount(4);
            page2.Select(x => x.OccurredAtUtc).Should().Equal(
                baseTime.AddMinutes(5),
                baseTime.AddMinutes(4),
                baseTime.AddMinutes(3),
                baseTime.AddMinutes(2));

            // No overlap with page 1.
            page2.Select(x => x.AuditEventId)
                .Intersect(page1.Select(x => x.AuditEventId))
                .Should().BeEmpty();

            // None of the concurrent inserts can appear on this page due to the cursor predicate.
            page2.Select(x => x.AuditEventId).Should().NotContain(new[] { newer1, newer2, sameTimeHigherId });
        }

        // A fresh query must see the concurrent inserts at the top.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var head = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    // We include enough rows to reach the additional "same time, higher id" insert,
                    // while still validating that truly newer rows appear at the top.
                    Limit: 6,
                    Offset: 0),
                CancellationToken.None);

            head.Should().HaveCount(6);
            head.Select(x => x.AuditEventId).Should().Contain(new[] { newer1, newer2, sameTimeHigherId });
            head[0].OccurredAtUtc.Should().Be(baseTime.AddMinutes(101));
            head[1].OccurredAtUtc.Should().Be(baseTime.AddMinutes(100));
        }
    }

    [Fact]
    public async Task QueryAsync_WithActionCodeFilter_AndTies_CursorPagingUsesStableOrderingWithinFilteredSet()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var entityId = DeterministicGuid.Create("audit|entity|cursor|action-filter");
        var t = new DateTime(2026, 1, 11, 12, 0, 0, DateTimeKind.Utc);

        // Mixed action codes at the same timestamp. We validate that ordering/tie-breaker and cursor paging
        // still work correctly within the filtered subset.
        var id1 = new Guid("00000000-0000-0000-0000-000000000001");
        var id2 = new Guid("00000000-0000-0000-0000-000000000002");
        var id3 = new Guid("00000000-0000-0000-0000-000000000003");
        var id4 = new Guid("00000000-0000-0000-0000-000000000004");
        var id5 = new Guid("00000000-0000-0000-0000-000000000005");
        var id6 = new Guid("00000000-0000-0000-0000-000000000006");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            // Odd IDs: "unpost"; Even IDs: "post".
            foreach (var (id, code) in new[]
                     {
                         (id1, "document.unpost"),
                         (id2, "document.post"),
                         (id3, "document.unpost"),
                         (id4, "document.post"),
                         (id5, "document.unpost"),
                         (id6, "document.post"),
                     })
            {
                await writer.WriteAsync(
                    new AuditEvent(
                        AuditEventId: id,
                        EntityKind: AuditEntityKind.Document,
                        EntityId: entityId,
                        ActionCode: code,
                        ActorUserId: null,
                        OccurredAtUtc: t,
                        CorrelationId: null,
                        MetadataJson: null,
                        Changes: Array.Empty<AuditFieldChange>()),
                    CancellationToken.None);
            }

            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var page1 = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    ActionCode: "document.post",
                    Limit: 1,
                    Offset: 0),
                CancellationToken.None);

            page1.Should().ContainSingle();
            page1[0].AuditEventId.Should().Be(id6);

            var cursor1 = page1.Single();
            var page2 = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    ActionCode: "document.post",
                    AfterOccurredAtUtc: cursor1.OccurredAtUtc,
                    AfterAuditEventId: cursor1.AuditEventId,
                    Limit: 1,
                    Offset: 0),
                CancellationToken.None);

            page2.Should().ContainSingle();
            page2[0].AuditEventId.Should().Be(id4);

            var cursor2 = page2.Single();
            var page3 = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    ActionCode: "document.post",
                    AfterOccurredAtUtc: cursor2.OccurredAtUtc,
                    AfterAuditEventId: cursor2.AuditEventId,
                    Limit: 10,
                    Offset: 0),
                CancellationToken.None);

            page3.Should().ContainSingle();
            page3[0].AuditEventId.Should().Be(id2);
        }
    }
}
