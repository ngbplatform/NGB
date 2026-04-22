using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_PostgresWriter_NoChanges_P0Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task WriteAuditEvent_WithNoChanges_PersistsEvent_AndNoChangeRows()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var entityId = Guid.CreateVersion7();
        var occurredAtUtc = new DateTime(2026, 1, 12, 12, 0, 0, DateTimeKind.Utc);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            var ev = new AuditEvent(
                AuditEventId: Guid.CreateVersion7(),
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId,
                ActionCode: "test.no_changes",
                ActorUserId: null,
                OccurredAtUtc: occurredAtUtc,
                CorrelationId: null,
                MetadataJson: null,
                Changes: Array.Empty<AuditFieldChange>());

            await writer.WriteAsync(ev, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    ActionCode: "test.no_changes",
                    Limit: 20,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            var loaded = events.Single();

            loaded.EntityId.Should().Be(entityId);
            loaded.ActionCode.Should().Be("test.no_changes");
            loaded.Changes.Should().BeEmpty();
        }
    }
}
