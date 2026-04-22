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
public sealed class AuditLog_Reader_ActionCodeWhitespace_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task QueryAsync_ActionCodeWhitespace_DoesNotApplyFilter()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var entityId = DeterministicGuid.Create("audit|actioncode|whitespace|entity");
        var baseTime = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            var events = new[]
            {
                new AuditEvent(
                    AuditEventId: DeterministicGuid.Create("audit|actioncode|a"),
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    ActionCode: "document.post",
                    ActorUserId: null,
                    OccurredAtUtc: baseTime.AddMinutes(0),
                    CorrelationId: null,
                    MetadataJson: null,
                    Changes: Array.Empty<AuditFieldChange>()),
                new AuditEvent(
                    AuditEventId: DeterministicGuid.Create("audit|actioncode|b"),
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    ActionCode: "document.unpost",
                    ActorUserId: null,
                    OccurredAtUtc: baseTime.AddMinutes(1),
                    CorrelationId: null,
                    MetadataJson: null,
                    Changes: Array.Empty<AuditFieldChange>()),
                new AuditEvent(
                    AuditEventId: DeterministicGuid.Create("audit|actioncode|c"),
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    ActionCode: "document.repost",
                    ActorUserId: null,
                    OccurredAtUtc: baseTime.AddMinutes(2),
                    CorrelationId: null,
                    MetadataJson: null,
                    Changes: Array.Empty<AuditFieldChange>())
            };

            foreach (var ev in events)
            {
                await writer.WriteAsync(ev, CancellationToken.None);
            }

            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            // Whitespace ActionCode should be treated as "not specified" and must not filter out records.
            var results = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    ActionCode: "   ",
                    Limit: 10,
                    Offset: 0),
                CancellationToken.None);

            results.Should().HaveCount(3);

            results[0].OccurredAtUtc.Should().Be(baseTime.AddMinutes(2));
            results[1].OccurredAtUtc.Should().Be(baseTime.AddMinutes(1));
            results[2].OccurredAtUtc.Should().Be(baseTime.AddMinutes(0));

            results.Select(x => x.ActionCode).Should().BeEquivalentTo(
                new[] { "document.post", "document.unpost", "document.repost" });
        }
    }
}
