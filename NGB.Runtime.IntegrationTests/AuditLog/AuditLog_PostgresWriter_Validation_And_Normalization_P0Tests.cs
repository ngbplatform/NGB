using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_PostgresWriter_Validation_And_Normalization_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime T0 = new(2026, 1, 19, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task WriteAsync_NullAuditEvent_ThrowsNgbArgumentRequiredException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

        var act = async () => await writer.WriteAsync(null!, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        ex.Which.ParamName.Should().Be("auditEvent");
    }

    [Fact]
    public async Task WriteAsync_WhenAuditEventIdEmpty_ThrowsNgbArgumentInvalidException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

        var ev = new AuditEvent(
            AuditEventId: Guid.Empty,
            EntityKind: AuditEntityKind.Document,
            EntityId: Guid.CreateVersion7(),
            ActionCode: "document.post",
            ActorUserId: null,
            OccurredAtUtc: T0,
            CorrelationId: null,
            MetadataJson: null,
            Changes: Array.Empty<AuditFieldChange>());

        var act = async () => await writer.WriteAsync(ev, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("auditEvent");
        ex.Which.Reason.Should().Be("AuditEventId must not be empty.");
    }

    [Fact]
    public async Task WriteAsync_WhenEntityIdEmpty_ThrowsNgbArgumentInvalidException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

        var ev = new AuditEvent(
            AuditEventId: Guid.CreateVersion7(),
            EntityKind: AuditEntityKind.Document,
            EntityId: Guid.Empty,
            ActionCode: "document.post",
            ActorUserId: null,
            OccurredAtUtc: T0,
            CorrelationId: null,
            MetadataJson: null,
            Changes: Array.Empty<AuditFieldChange>());

        var act = async () => await writer.WriteAsync(ev, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("auditEvent");
        ex.Which.Reason.Should().Be("EntityId must not be empty.");
    }

    [Fact]
    public async Task WriteAsync_WhenActionCodeWhitespace_ThrowsNgbArgumentInvalidException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

        var ev = new AuditEvent(
            AuditEventId: Guid.CreateVersion7(),
            EntityKind: AuditEntityKind.Document,
            EntityId: Guid.CreateVersion7(),
            ActionCode: "   ",
            ActorUserId: null,
            OccurredAtUtc: T0,
            CorrelationId: null,
            MetadataJson: null,
            Changes: Array.Empty<AuditFieldChange>());

        var act = async () => await writer.WriteAsync(ev, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("auditEvent");
        ex.Which.Reason.Should().Be("ActionCode must not be empty.");
    }

    [Fact]
    public async Task WriteAsync_WhenOccurredAtNotUtc_ThrowsNgbArgumentInvalidException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

        var ev = new AuditEvent(
            AuditEventId: Guid.CreateVersion7(),
            EntityKind: AuditEntityKind.Document,
            EntityId: Guid.CreateVersion7(),
            ActionCode: "document.post",
            ActorUserId: null,
            OccurredAtUtc: new DateTime(2026, 1, 19, 0, 0, 0, DateTimeKind.Local),
            CorrelationId: null,
            MetadataJson: null,
            Changes: Array.Empty<AuditFieldChange>());

        var act = async () => await writer.WriteAsync(ev, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("OccurredAtUtc");
        ex.Which.Reason.Should().Contain("must be UTC");
    }

    [Fact]
    public async Task WriteAsync_WhenChangeFieldPathWhitespace_Throws_AndRollbackLeavesNoRows()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var entityId = DeterministicGuid.Create("audit|writer|invalid-fieldpath|entity");
        var eventId = DeterministicGuid.Create("audit|writer|invalid-fieldpath|event");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            var ev = new AuditEvent(
                AuditEventId: eventId,
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId,
                ActionCode: "document.post",
                ActorUserId: null,
                OccurredAtUtc: T0,
                CorrelationId: null,
                MetadataJson: null,
                Changes: new[] { new AuditFieldChange("   ", null, null) });

            var act = async () => await writer.WriteAsync(ev, CancellationToken.None);

            var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
            ex.Which.ParamName.Should().Be("auditEvent");
            ex.Which.Reason.Should().Be("FieldPath must not be empty.");

            await uow.RollbackAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var results = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            results.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task WriteAsync_WhitespaceMetadataAndChangeValues_AreNormalizedToNull()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var entityId = DeterministicGuid.Create("audit|writer|normalize|entity");
        var eventId = DeterministicGuid.Create("audit|writer|normalize|event");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            var ev = new AuditEvent(
                AuditEventId: eventId,
                EntityKind: AuditEntityKind.Document,
                EntityId: entityId,
                ActionCode: "  document.post  ",
                ActorUserId: null,
                OccurredAtUtc: T0,
                CorrelationId: null,
                MetadataJson: "   ",
                Changes: new[] { new AuditFieldChange("  status  ", "  ", "\n") });

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
                    Limit: 10,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            var loaded = events.Single();

            loaded.ActionCode.Should().Be("document.post");
            loaded.MetadataJson.Should().BeNull();

            loaded.Changes.Should().HaveCount(1);
            loaded.Changes[0].FieldPath.Should().Be("status");
            loaded.Changes[0].OldValueJson.Should().BeNull();
            loaded.Changes[0].NewValueJson.Should().BeNull();
        }
    }
}
