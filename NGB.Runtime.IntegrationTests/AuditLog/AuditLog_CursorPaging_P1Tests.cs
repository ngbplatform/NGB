using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_CursorPaging_P1Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task QueryAsync_CursorPaging_ReturnsStableNonOverlappingPages()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var entityId = DeterministicGuid.Create("audit|entity|cursor");
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Seed 5 events (time increases). Query returns them in DESC order.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            for (var i = 0; i < 5; i++)
            {
                var ev = new AuditEvent(
                    AuditEventId: DeterministicGuid.Create($"audit|event|{i}"),
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

            await uow.CommitAsync();
        }

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
                    Limit: 2,
                    Offset: 0),
                CancellationToken.None);

            page1.Should().HaveCount(2);
            page1[0].OccurredAtUtc.Should().Be(baseTime.AddMinutes(4));
            page1[1].OccurredAtUtc.Should().Be(baseTime.AddMinutes(3));

            var cursor1 = page1[^1];

            page2 = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    AfterOccurredAtUtc: cursor1.OccurredAtUtc,
                    AfterAuditEventId: cursor1.AuditEventId,
                    Limit: 2,
                    Offset: 0),
                CancellationToken.None);

            page2.Should().HaveCount(2);
            page2[0].OccurredAtUtc.Should().Be(baseTime.AddMinutes(2));
            page2[1].OccurredAtUtc.Should().Be(baseTime.AddMinutes(1));

            var cursor2 = page2[^1];

            page3 = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    AfterOccurredAtUtc: cursor2.OccurredAtUtc,
                    AfterAuditEventId: cursor2.AuditEventId,
                    Limit: 10,
                    Offset: 0),
                CancellationToken.None);

            page3.Should().HaveCount(1);
            page3[0].OccurredAtUtc.Should().Be(baseTime.AddMinutes(0));
        }

        var all = page1.Concat(page2).Concat(page3).ToList();
        all.Select(x => x.AuditEventId).Distinct().Should().HaveCount(5);
    }

    [Fact]
    public async Task QueryAsync_CursorPaging_RequiresBothCursorFields()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var act1 = async () => await reader.QueryAsync(
            new AuditLogQuery(AfterOccurredAtUtc: DateTime.UtcNow),
            CancellationToken.None);

        await act1.Should().ThrowAsync<NgbArgumentInvalidException>();

        var act2 = async () => await reader.QueryAsync(
            new AuditLogQuery(AfterAuditEventId: Guid.CreateVersion7()),
            CancellationToken.None);

        await act2.Should().ThrowAsync<NgbArgumentInvalidException>();
    }
}
