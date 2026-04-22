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
public sealed class AuditLog_ReaderCursorPaging_MultiPage_NoGaps_P2Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static Guid HexGuid(int value)
        => Guid.Parse($"00000000-0000-0000-0000-{value:X12}");

    [Fact]
    public async Task QueryAsync_CursorPaging_MultiPage_NoGaps_NoOverlap_WithTieBreakerAcrossPages()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var entityId = DeterministicGuid.Create("audit|entity|multi-page");
        var noiseEntityId = DeterministicGuid.Create("audit|entity|noise");

        var baseTime = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        // 7 events for the target entity. Some share the same OccurredAtUtc (tie-breaker by AuditEventId DESC).
        // Expected stable ordering (DESC):
        //  t+4 id07, t+4 id06, t+3 id05, t+2 id04, t+2 id03, t+1 id02, t+0 id01
        var expected = new (DateTime t, Guid id)[]
        {
            (baseTime.AddMinutes(4), HexGuid(0x07)),
            (baseTime.AddMinutes(4), HexGuid(0x06)),
            (baseTime.AddMinutes(3), HexGuid(0x05)),
            (baseTime.AddMinutes(2), HexGuid(0x04)),
            (baseTime.AddMinutes(2), HexGuid(0x03)),
            (baseTime.AddMinutes(1), HexGuid(0x02)),
            (baseTime.AddMinutes(0), HexGuid(0x01)),
        };

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            // Noise: should never appear in the query (different entity_id).
            await writer.WriteAsync(
                new AuditEvent(
                    AuditEventId: HexGuid(0x99),
                    EntityKind: AuditEntityKind.Document,
                    EntityId: noiseEntityId,
                    ActionCode: "document.post",
                    ActorUserId: null,
                    OccurredAtUtc: baseTime.AddMinutes(4),
                    CorrelationId: null,
                    MetadataJson: null,
                    Changes: Array.Empty<AuditFieldChange>()),
                CancellationToken.None);

            // Target events.
            foreach (var (t, id) in expected.Reverse())
            {
                IReadOnlyList<AuditFieldChange> changes = Array.Empty<AuditFieldChange>();

                // Add a few changes to a mid event so we can verify they load and preserve order.
                if (id == HexGuid(0x05))
                {
                    changes = new[]
                    {
                        new AuditFieldChange("name", "\"Old\"", "\"New\""),
                        new AuditFieldChange("status", "\"draft\"", "\"posted\""),
                        new AuditFieldChange("amount", "1", "2"),
                    };
                }

                await writer.WriteAsync(
                    new AuditEvent(
                        AuditEventId: id,
                        EntityKind: AuditEntityKind.Document,
                        EntityId: entityId,
                        ActionCode: "document.post",
                        ActorUserId: null,
                        OccurredAtUtc: t,
                        CorrelationId: null,
                        MetadataJson: null,
                        Changes: changes),
                    CancellationToken.None);
            }

            await uow.CommitAsync();
        }

        var collected = new List<AuditEvent>();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            DateTime? cursorTime = null;
            Guid? cursorId = null;

            while (true)
            {
                var page = await reader.QueryAsync(
                    new AuditLogQuery(
                        EntityKind: AuditEntityKind.Document,
                        EntityId: entityId,
                        AfterOccurredAtUtc: cursorTime,
                        AfterAuditEventId: cursorId,
                        Limit: 2,
                        Offset: 0),
                    CancellationToken.None);

                if (page.Count == 0)
                    break;

                collected.AddRange(page);

                // Move cursor to the last row of the page.
                cursorTime = page[^1].OccurredAtUtc;
                cursorId = page[^1].AuditEventId;
            }
        }

        collected.Should().HaveCount(7);

        // No overlap.
        collected.Select(x => x.AuditEventId).Distinct().Should().HaveCount(7);

        // Exact expected stable ordering.
        collected.Select(x => (x.OccurredAtUtc, x.AuditEventId)).Should().Equal(expected);

        // Verify changes loaded and preserve deterministic order for the event with changes.
        var withChanges = collected.Single(x => x.AuditEventId == HexGuid(0x05));
        withChanges.Changes.Select(x => x.FieldPath).Should().Equal("name", "status", "amount");
        withChanges.Changes.Should().HaveCount(3);

        // Events without changes still have an empty list.
        collected.Where(x => x.AuditEventId != HexGuid(0x05))
            .All(x => x.Changes.Count == 0)
            .Should().BeTrue();
    }
}
