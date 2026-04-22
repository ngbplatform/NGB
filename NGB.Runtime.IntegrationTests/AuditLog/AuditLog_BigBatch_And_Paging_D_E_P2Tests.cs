using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_BigBatch_And_Paging_D_E_P2Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task WriteAuditEvent_With500Changes_PersistsAllChangeRows_AndReaderReturnsInDeterministicOrder()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var entityId = Guid.CreateVersion7();
        var auditEventId = Guid.CreateVersion7();
        var occurredAtUtc = new DateTime(2026, 01, 20, 12, 0, 0, DateTimeKind.Utc);

        var changes = new List<AuditFieldChange>(capacity: 500);
        for (var i = 1; i <= 500; i++)
        {
            changes.Add(new AuditFieldChange(
                FieldPath: $"f{i:0000}",
                OldValueJson: (i - 1).ToString(),
                NewValueJson: i.ToString()));
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            var ev = new AuditEvent(
                AuditEventId: auditEventId,
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId,
                ActionCode: "test.big_batch",
                ActorUserId: null,
                OccurredAtUtc: occurredAtUtc,
                CorrelationId: null,
                MetadataJson: null,
                Changes: changes);

            await writer.WriteAsync(ev, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        // Assert: rows exist in DB and ordinals are 1..500.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var ordinals = (await uow.Connection.QueryAsync<int>(
                "SELECT ordinal FROM platform_audit_event_changes WHERE audit_event_id = @Id ORDER BY ordinal;",
                new { Id = auditEventId })).AsList();

            ordinals.Should().Equal(Enumerable.Range(1, 500));

            var fieldPaths = (await uow.Connection.QueryAsync<string>(
                "SELECT field_path FROM platform_audit_event_changes WHERE audit_event_id = @Id ORDER BY ordinal;",
                new { Id = auditEventId })).AsList();

            fieldPaths.Should().HaveCount(500);
            fieldPaths[0].Should().Be("f0001");
            fieldPaths[^1].Should().Be("f0500");
        }

        // Assert: reader returns changes in the same deterministic order.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(new AuditLogQuery(
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId,
                ActionCode: "test.big_batch",
                Limit: 10,
                Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            var loaded = events.Single();

            loaded.AuditEventId.Should().Be(auditEventId);
            loaded.Changes.Should().HaveCount(500);
            loaded.Changes[0].FieldPath.Should().Be("f0001");
            loaded.Changes[0].OldValueJson.Should().Be("0");
            loaded.Changes[0].NewValueJson.Should().Be("1");
            loaded.Changes[^1].FieldPath.Should().Be("f0500");
            loaded.Changes[^1].OldValueJson.Should().Be("499");
            loaded.Changes[^1].NewValueJson.Should().Be("500");
        }
    }

    [Fact]
    public async Task Query_WithOffsetPaging_ReturnsStableOrdering_AndLoadsChangesForEachEvent()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var entityId = Guid.CreateVersion7();
        var baseUtc = new DateTime(2026, 01, 20, 13, 0, 0, DateTimeKind.Utc);

        // Insert 5 events with unique timestamps.
        var written = new List<(Guid AuditEventId, DateTime OccurredAtUtc)>(capacity: 5);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            for (var i = 0; i < 5; i++)
            {
                var id = Guid.CreateVersion7();
                var occurredAt = baseUtc.AddMinutes(i);
                written.Add((id, occurredAt));

                var ev = new AuditEvent(
                    AuditEventId: id,
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    ActionCode: "test.offset_paging",
                    ActorUserId: null,
                    OccurredAtUtc: occurredAt,
                    CorrelationId: null,
                    MetadataJson: null,
                    Changes:
                    [
                        new AuditFieldChange("seq", i.ToString(), (i + 1).ToString()),
                        new AuditFieldChange("marker", "0", "1")
                    ]);

                await writer.WriteAsync(ev, CancellationToken.None);
            }

            await uow.CommitAsync(CancellationToken.None);
        }

        // Expected ordering: newest first.
        var expectedOrder = written
            .OrderByDescending(x => x.OccurredAtUtc)
            .Select(x => x.AuditEventId)
            .ToArray();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var page1 = await reader.QueryAsync(new AuditLogQuery(
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId,
                ActionCode: "test.offset_paging",
                Limit: 2,
                Offset: 0),
                CancellationToken.None);

            page1.Select(x => x.AuditEventId).Should().Equal(expectedOrder.Take(2));
            page1.All(x => x.Changes.Count == 2).Should().BeTrue();

            var page2 = await reader.QueryAsync(new AuditLogQuery(
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId,
                ActionCode: "test.offset_paging",
                Limit: 2,
                Offset: 2),
                CancellationToken.None);

            page2.Select(x => x.AuditEventId).Should().Equal(expectedOrder.Skip(2).Take(2));
            page2.All(x => x.Changes.Count == 2).Should().BeTrue();

            var page3 = await reader.QueryAsync(new AuditLogQuery(
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId,
                ActionCode: "test.offset_paging",
                Limit: 2,
                Offset: 4),
                CancellationToken.None);

            page3.Select(x => x.AuditEventId).Should().Equal(expectedOrder.Skip(4).Take(1));
            page3.Single().Changes.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task Query_WithFromToAndCursor_PagesWithinRange_NoOverlap_AndIncludesBounds()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var entityId = Guid.CreateVersion7();
        var baseUtc = new DateTime(2026, 01, 20, 14, 0, 0, DateTimeKind.Utc);

        // Create 6 events at 0..5 minutes.
        var events = new List<(Guid AuditEventId, DateTime OccurredAtUtc)>(capacity: 6);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            for (var i = 0; i < 6; i++)
            {
                var id = Guid.CreateVersion7();
                var occurredAt = baseUtc.AddMinutes(i);
                events.Add((id, occurredAt));

                var ev = new AuditEvent(
                    AuditEventId: id,
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    ActionCode: "test.range_cursor",
                    ActorUserId: null,
                    OccurredAtUtc: occurredAt,
                    CorrelationId: null,
                    MetadataJson: null,
                    Changes: [ new AuditFieldChange("i", i.ToString(), (i + 1).ToString()) ]);

                await writer.WriteAsync(ev, CancellationToken.None);
            }

            await uow.CommitAsync(CancellationToken.None);
        }

        var fromUtc = baseUtc.AddMinutes(1); // include i=1
        var toUtc = baseUtc.AddMinutes(4);   // include i=4

        // Expected set within [from..to] ordered newest first.
        var expected = events
            .Where(x => x.OccurredAtUtc >= fromUtc && x.OccurredAtUtc <= toUtc)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Select(x => x.AuditEventId)
            .ToArray();

        expected.Should().HaveCount(4);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            // Page 1
            var page1 = await reader.QueryAsync(new AuditLogQuery(
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId,
                ActionCode: "test.range_cursor",
                FromUtc: fromUtc,
                ToUtc: toUtc,
                Limit: 2,
                Offset: 0),
                CancellationToken.None);

            page1.Select(x => x.AuditEventId).Should().Equal(expected.Take(2));
            page1.Should().OnlyContain(x => x.Changes.Count == 1);

            var cursor = page1.Last();

            // Page 2
            var page2 = await reader.QueryAsync(new AuditLogQuery(
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId,
                ActionCode: "test.range_cursor",
                FromUtc: fromUtc,
                ToUtc: toUtc,
                AfterOccurredAtUtc: cursor.OccurredAtUtc,
                AfterAuditEventId: cursor.AuditEventId,
                Limit: 2,
                Offset: 0),
                CancellationToken.None);

            page2.Select(x => x.AuditEventId).Should().Equal(expected.Skip(2).Take(2));
            page2.Should().OnlyContain(x => x.Changes.Count == 1);

            // Page 3 (empty)
            var cursor2 = page2.Last();
            var page3 = await reader.QueryAsync(new AuditLogQuery(
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId,
                ActionCode: "test.range_cursor",
                FromUtc: fromUtc,
                ToUtc: toUtc,
                AfterOccurredAtUtc: cursor2.OccurredAtUtc,
                AfterAuditEventId: cursor2.AuditEventId,
                Limit: 2,
                Offset: 0),
                CancellationToken.None);

            page3.Should().BeEmpty();

            // Combined pages are exactly the expected set (no overlap, no gaps).
            var combined = page1.Concat(page2).Select(x => x.AuditEventId).ToArray();
            combined.Should().Equal(expected);

            // Boundaries are included.
            combined.Should().Contain(expected.First()); // ToUtc (newest in range)
            combined.Should().Contain(expected.Last());  // FromUtc (oldest in range)
        }
    }
}
