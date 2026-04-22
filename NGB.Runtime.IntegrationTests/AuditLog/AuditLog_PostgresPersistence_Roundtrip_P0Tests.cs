using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_PostgresPersistence_Roundtrip_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task UpsertUser_ThenWriteEventWithChanges_ThenQueryBack()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var occurredAtUtc = new DateTime(2026, 01, 19, 0, 0, 0, DateTimeKind.Utc);

        var entityId = Guid.CreateVersion7();
        var auditEventId = Guid.CreateVersion7();

        // Act (write)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

            await uow.BeginTransactionAsync();

            var userId = await users.UpsertAsync(
                authSubject: "keycloak-sub-123",
                email: "user@example.com",
                displayName: "Test User",
                isActive: true);

            var auditEvent = new AuditEvent(
                AuditEventId: auditEventId,
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId,
                ActionCode: "document.post",
                ActorUserId: userId,
                OccurredAtUtc: occurredAtUtc,
                CorrelationId: Guid.CreateVersion7(),
                MetadataJson: "{\"source\":\"integration-test\"}",
                Changes:
                [
                    new AuditFieldChange("status", "\"Draft\"", "\"Posted\""),
                    new AuditFieldChange("amount", "1", "2")
                ]);

            await writer.WriteAsync(auditEvent);
            await uow.CommitAsync();
        }

        // Assert (read)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var results = await reader.QueryAsync(new AuditLogQuery(
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId,
                Limit: 10,
                Offset: 0));

            results.Should().HaveCount(1);
            var e = results.Single();

            e.AuditEventId.Should().Be(auditEventId);
            e.EntityKind.Should().Be(AuditEntityKind.Document);
            e.EntityId.Should().Be(entityId);
            e.ActionCode.Should().Be("document.post");
            e.OccurredAtUtc.Should().Be(occurredAtUtc);
            e.MetadataJson.Should().NotBeNull();

            e.Changes.Should().HaveCount(2);
            e.Changes[0].FieldPath.Should().Be("status");
            e.Changes[0].OldValueJson.Should().Be("\"Draft\"");
            e.Changes[0].NewValueJson.Should().Be("\"Posted\"");
        }
    }

    [Fact]
    public async Task WriteAuditEvent_WithoutActiveTransaction_Throws()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

        var auditEvent = new AuditEvent(
            AuditEventId: Guid.CreateVersion7(),
            EntityKind: AuditEntityKind.Catalog,
            EntityId: Guid.CreateVersion7(),
            ActionCode: "catalog.create",
            ActorUserId: null,
            OccurredAtUtc: new DateTime(2026, 01, 19, 0, 0, 0, DateTimeKind.Utc),
            CorrelationId: null,
            MetadataJson: null,
            Changes: []);

        // Act
        var act = async () => await writer.WriteAsync(auditEvent);

        // Assert
        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("This operation requires an active transaction.");
    }
}
