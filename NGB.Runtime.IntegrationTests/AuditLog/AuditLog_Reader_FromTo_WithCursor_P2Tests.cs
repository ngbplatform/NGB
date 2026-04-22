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
public sealed class AuditLog_Reader_FromTo_WithCursor_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task QueryAsync_FromTo_WithCursorPaging_RespectsBounds_AndReturnsStablePages()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var entityId = DeterministicGuid.Create("audit|fromto|entity");
        var baseTime = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        // Seed events at minutes 0..9.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            for (var i = 0; i < 10; i++)
            {
                var ev = new AuditEvent(
                    AuditEventId: DeterministicGuid.Create($"audit|fromto|event|{i}"),
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

        var fromUtc = baseTime.AddMinutes(3); // inclusive
        var toUtc = baseTime.AddMinutes(7);   // inclusive

        IReadOnlyList<AuditEvent> page1;
        IReadOnlyList<AuditEvent> page2;
        IReadOnlyList<AuditEvent> page3;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            page1 = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    FromUtc: fromUtc,
                    ToUtc: toUtc,
                    Limit: 3,
                    Offset: 0),
                CancellationToken.None);

            // Range includes minutes 3..7 (5 events). Ordered DESC => 7,6,5,4,3.
            page1.Should().HaveCount(3);
            page1[0].OccurredAtUtc.Should().Be(baseTime.AddMinutes(7));
            page1[1].OccurredAtUtc.Should().Be(baseTime.AddMinutes(6));
            page1[2].OccurredAtUtc.Should().Be(baseTime.AddMinutes(5));

            var cursor1 = page1[^1];

            page2 = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    FromUtc: fromUtc,
                    ToUtc: toUtc,
                    AfterOccurredAtUtc: cursor1.OccurredAtUtc,
                    AfterAuditEventId: cursor1.AuditEventId,
                    Limit: 10,
                    Offset: 0),
                CancellationToken.None);

            page2.Should().HaveCount(2);
            page2[0].OccurredAtUtc.Should().Be(baseTime.AddMinutes(4));
            page2[1].OccurredAtUtc.Should().Be(baseTime.AddMinutes(3));

            var cursor2 = page2[^1];

            page3 = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    FromUtc: fromUtc,
                    ToUtc: toUtc,
                    AfterOccurredAtUtc: cursor2.OccurredAtUtc,
                    AfterAuditEventId: cursor2.AuditEventId,
                    Limit: 10,
                    Offset: 0),
                CancellationToken.None);

            page3.Should().BeEmpty();
        }

        // Ensure non-overlapping pages.
        var all = page1.Concat(page2).Concat(page3).ToList();
        all.Should().HaveCount(5);
        all.Select(x => x.AuditEventId).Distinct().Should().HaveCount(5);
        all.Min(x => x.OccurredAtUtc).Should().Be(fromUtc);
        all.Max(x => x.OccurredAtUtc).Should().Be(toUtc);
    }
}
