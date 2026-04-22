using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_CursorPaging_TieBreaker_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task QueryAsync_WhenMultipleEventsShareSameTimestamp_UsesAuditEventIdAsTieBreaker_AndCursorPagingWorks()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var entityId = Guid.CreateVersion7();
        var t = new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

        // Use predictable UUID ordering to validate the tie-breaker.
        var id1 = new Guid("00000000-0000-0000-0000-000000000001");
        var id2 = new Guid("00000000-0000-0000-0000-000000000002");
        var id3 = new Guid("00000000-0000-0000-0000-000000000003");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            foreach (var id in new[] { id1, id2, id3 })
            {
                var ev = new AuditEvent(
                    AuditEventId: id,
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    ActionCode: "document.post",
                    ActorUserId: null,
                    OccurredAtUtc: t,
                    CorrelationId: null,
                    MetadataJson: null,
                    Changes: Array.Empty<AuditFieldChange>());

                await writer.WriteAsync(ev, CancellationToken.None);
            }

            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            // Page 1: the largest UUID must come first due to stable ordering:
            // (occurred_at_utc DESC, audit_event_id DESC).
            var page1 = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    Limit: 1,
                    Offset: 0),
                CancellationToken.None);

            page1.Should().ContainSingle();
            page1[0].AuditEventId.Should().Be(id3);

            // Page 2: cursor should skip id3 and return id2.
            var cursor1 = page1.Single();
            var page2 = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    AfterOccurredAtUtc: cursor1.OccurredAtUtc,
                    AfterAuditEventId: cursor1.AuditEventId,
                    Limit: 1,
                    Offset: 0),
                CancellationToken.None);

            page2.Should().ContainSingle();
            page2[0].AuditEventId.Should().Be(id2);

            // Page 3: cursor should return id1.
            var cursor2 = page2.Single();
            var page3 = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    AfterOccurredAtUtc: cursor2.OccurredAtUtc,
                    AfterAuditEventId: cursor2.AuditEventId,
                    Limit: 10,
                    Offset: 0),
                CancellationToken.None);

            page3.Should().ContainSingle();
            page3[0].AuditEventId.Should().Be(id1);
        }
    }
}
