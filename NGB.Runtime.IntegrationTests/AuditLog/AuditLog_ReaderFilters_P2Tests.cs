using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_ReaderFilters_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private sealed record SeedData(
        Guid User1Id,
        Guid User2Id,
        Guid EntityId1,
        Guid EntityId2,
        DateTime BaseTimeUtc,
        Guid EventA,
        Guid EventB,
        Guid EventC,
        Guid EventD,
        Guid EventE,
        Guid EventF);

    private static async Task<SeedData> SeedAsync(IHost host)
    {
        var entityId1 = DeterministicGuid.Create("audit|filters|entity|1");
        var entityId2 = DeterministicGuid.Create("audit|filters|entity|2");
        var baseTimeUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        var eventA = DeterministicGuid.Create("audit|filters|event|A");
        var eventB = DeterministicGuid.Create("audit|filters|event|B");
        var eventC = DeterministicGuid.Create("audit|filters|event|C");
        var eventD = DeterministicGuid.Create("audit|filters|event|D");
        var eventE = DeterministicGuid.Create("audit|filters|event|E");
        var eventF = DeterministicGuid.Create("audit|filters|event|F");

        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
        var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

        await uow.BeginTransactionAsync();

        var user1Id = await users.UpsertAsync(
            authSubject: "keycloak-sub-filters-1",
            email: "user1@example.com",
            displayName: "User 1",
            isActive: true);

        var user2Id = await users.UpsertAsync(
            authSubject: "keycloak-sub-filters-2",
            email: "user2@example.com",
            displayName: "User 2",
            isActive: true);

        // Document + entityId1
        await writer.WriteAsync(new AuditEvent(
            AuditEventId: eventA,
            EntityKind: AuditEntityKind.Document,
            EntityId: entityId1,
            ActionCode: "document.post",
            ActorUserId: user1Id,
            OccurredAtUtc: baseTimeUtc.AddMinutes(1),
            CorrelationId: null,
            MetadataJson: null,
            Changes: Array.Empty<AuditFieldChange>()));

        await writer.WriteAsync(new AuditEvent(
            AuditEventId: eventB,
            EntityKind: AuditEntityKind.Document,
            EntityId: entityId1,
            ActionCode: "document.post",
            ActorUserId: user2Id,
            OccurredAtUtc: baseTimeUtc.AddMinutes(2),
            CorrelationId: null,
            MetadataJson: null,
            Changes: Array.Empty<AuditFieldChange>()));

        await writer.WriteAsync(new AuditEvent(
            AuditEventId: eventC,
            EntityKind: AuditEntityKind.Document,
            EntityId: entityId1,
            ActionCode: "document.unpost",
            ActorUserId: user1Id,
            OccurredAtUtc: baseTimeUtc.AddMinutes(3),
            CorrelationId: null,
            MetadataJson: null,
            Changes: Array.Empty<AuditFieldChange>()));

        // Document + entityId2
        await writer.WriteAsync(new AuditEvent(
            AuditEventId: eventD,
            EntityKind: AuditEntityKind.Document,
            EntityId: entityId2,
            ActionCode: "document.post",
            ActorUserId: user1Id,
            OccurredAtUtc: baseTimeUtc.AddMinutes(4),
            CorrelationId: null,
            MetadataJson: null,
            Changes: Array.Empty<AuditFieldChange>()));

        // Non-document event should not leak into document queries.
        await writer.WriteAsync(new AuditEvent(
            AuditEventId: eventE,
            EntityKind: AuditEntityKind.Catalog,
            EntityId: entityId1,
            ActionCode: "catalog.create",
            ActorUserId: user1Id,
            OccurredAtUtc: baseTimeUtc.AddMinutes(5),
            CorrelationId: null,
            MetadataJson: null,
            Changes: Array.Empty<AuditFieldChange>()));

        // Document + entityId1 + NULL actor
        await writer.WriteAsync(new AuditEvent(
            AuditEventId: eventF,
            EntityKind: AuditEntityKind.Document,
            EntityId: entityId1,
            ActionCode: "document.post",
            ActorUserId: null,
            OccurredAtUtc: baseTimeUtc.AddMinutes(6),
            CorrelationId: null,
            MetadataJson: null,
            Changes: Array.Empty<AuditFieldChange>()));

        await uow.CommitAsync();

        return new SeedData(
            User1Id: user1Id,
            User2Id: user2Id,
            EntityId1: entityId1,
            EntityId2: entityId2,
            BaseTimeUtc: baseTimeUtc,
            EventA: eventA,
            EventB: eventB,
            EventC: eventC,
            EventD: eventD,
            EventE: eventE,
            EventF: eventF);
    }

    [Fact]
    public async Task QueryAsync_FilterByActorUserId_ReturnsOnlyMatchingActor()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var seed = await SeedAsync(host);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        // Act
        var results = await reader.QueryAsync(new AuditLogQuery(
            EntityKind: AuditEntityKind.Document,
            ActorUserId: seed.User1Id,
            Limit: 100,
            Offset: 0));

        // Assert
        results.Select(x => x.AuditEventId)
            .Should().Equal(seed.EventD, seed.EventC, seed.EventA);

        results.Should().OnlyContain(x => x.ActorUserId == seed.User1Id);
    }

    [Fact]
    public async Task QueryAsync_FilterByActionCode_TrimsAndMatchesExact()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var seed = await SeedAsync(host);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        // Act
        var results = await reader.QueryAsync(new AuditLogQuery(
            EntityKind: AuditEntityKind.Document,
            EntityId: seed.EntityId1,
            ActionCode: "  document.post  ",
            Limit: 100,
            Offset: 0));

        // Assert (F,B,A order by time desc)
        results.Select(x => x.AuditEventId)
            .Should().Equal(seed.EventF, seed.EventB, seed.EventA);

        results.Should().OnlyContain(x => x.ActionCode == "document.post");
    }

    [Fact]
    public async Task QueryAsync_FilterByFromToUtc_IsInclusive_OnBothSides()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var seed = await SeedAsync(host);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var fromUtc = seed.BaseTimeUtc.AddMinutes(2); // EventB
        var toUtc = seed.BaseTimeUtc.AddMinutes(4);   // EventD

        // Act
        var results = await reader.QueryAsync(new AuditLogQuery(
            EntityKind: AuditEntityKind.Document,
            FromUtc: fromUtc,
            ToUtc: toUtc,
            Limit: 100,
            Offset: 0));

        // Assert: (D,C,B)
        results.Select(x => x.AuditEventId)
            .Should().Equal(seed.EventD, seed.EventC, seed.EventB);

        results.Should().OnlyContain(x => x.OccurredAtUtc >= fromUtc && x.OccurredAtUtc <= toUtc);
    }
}
