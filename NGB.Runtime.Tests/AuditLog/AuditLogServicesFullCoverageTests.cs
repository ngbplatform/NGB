using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Core.AuditLog;
using NGB.Core.Catalogs;
using NGB.Core.Documents;
using NGB.Core.Security;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.CurrentActor;
using NGB.Runtime.Security;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.AuditLog;

public sealed class AuditLogServicesFullCoverageTests
{
    private static readonly DateTime Now = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    [Fact]
    public async Task Query_RejectsInvalidEntityCursorAndLimits()
    {
        var sut = new AuditLogQueryService(Mock.Of<IAuditEventReader>());

        await ((Func<Task>)(() => sut.GetEntityAuditLogAsync(
                AuditEntityKind.Document, Guid.Empty, null, null, 1, default)))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await ((Func<Task>)(() => sut.GetEntityAuditLogAsync(
                AuditEntityKind.Document, Guid.CreateVersion7(), DateTime.SpecifyKind(Now, DateTimeKind.Local), null, 1, default)))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await ((Func<Task>)(() => sut.GetEntityAuditLogAsync(
                AuditEntityKind.Document, Guid.CreateVersion7(), null, null, 0, default)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => sut.GetEntityAuditLogAsync(
                AuditEntityKind.Document, Guid.CreateVersion7(), null, null, 101, default)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Query_MapsEmptyAndAllActorResolutionPathsAndCursor()
    {
        var entityId = Guid.CreateVersion7();
        var knownId = Guid.CreateVersion7();
        var unknownId = Guid.CreateVersion7();
        var reader = new Mock<IAuditEventReader>(MockBehavior.Strict);
        reader.SetupSequence(x => x.QueryAsync(It.IsAny<AuditLogQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([
                Event(entityId, null, "none"),
                Event(entityId, Guid.Empty, "empty"),
                Event(entityId, knownId, "known"),
                Event(entityId, unknownId, "unknown")
            ]);
        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        users.Setup(x => x.GetByIdsAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { knownId, unknownId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PlatformUser>
            {
                [knownId] = new(knownId, "subject", "known@example.com", "Known", true, Now, Now)
            });
        var withoutUsers = new AuditLogQueryService(reader.Object);
        var withUsers = new AuditLogQueryService(reader.Object, users.Object);

        var empty = await withoutUsers.GetEntityAuditLogAsync(
            AuditEntityKind.Document, entityId, null, null, 100, default);
        empty.Items.Should().BeEmpty();
        empty.NextCursor.Should().BeNull();

        var page = await withUsers.GetEntityAuditLogAsync(
            AuditEntityKind.Document, entityId, Now, Guid.CreateVersion7(), 100, default);
        page.Items.Should().HaveCount(4);
        page.Items[0].Actor.Should().BeNull();
        page.Items[1].Actor!.UserId.Should().Be(Guid.Empty);
        page.Items[2].Actor.Should().BeEquivalentTo(new { UserId = knownId, DisplayName = "Known", Email = "known@example.com" });
        page.Items[3].Actor.Should().BeEquivalentTo(new { UserId = unknownId, DisplayName = (string?)null, Email = (string?)null });
        page.Items[0].Changes.Should().ContainSingle();
        page.NextCursor!.AuditEventId.Should().Be(page.Items[^1].AuditEventId);
        users.VerifyAll();
    }

    [Fact]
    public async Task Query_WithUserRepositoryButNoResolvableActors_SkipsUserLookup()
    {
        var entityId = Guid.CreateVersion7();
        var reader = new Mock<IAuditEventReader>();
        reader.Setup(x => x.QueryAsync(It.IsAny<AuditLogQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Event(entityId, null, "none"), Event(entityId, Guid.Empty, "empty")]);
        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);

        var page = await new AuditLogQueryService(reader.Object, users.Object)
            .GetEntityAuditLogAsync(AuditEntityKind.Document, entityId, null, null, 2, default);

        page.Items.Should().HaveCount(2);
        users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PermissionAwareQuery_CoversSystemOverrideEveryEntityKindAndMissingFallbacks()
    {
        var reader = new Mock<IAuditEventReader>();
        reader.Setup(x => x.QueryAsync(It.IsAny<AuditLogQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var inner = new AuditLogQueryService(reader.Object);
        var systemAccess = new Mock<INgbAccessChecker>(MockBehavior.Strict);
        systemAccess.Setup(x => x.HasAsync(
                NgbResourceKinds.System, NgbPermissionResources.Audit, NgbPermissionActions.View,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var systemSut = new PermissionAwareAuditLogQueryService(
            inner, systemAccess.Object, Mock.Of<IDocumentRepository>(), Mock.Of<ICatalogRepository>());
        await systemSut.GetEntityAuditLogAsync(
            AuditEntityKind.Document, Guid.CreateVersion7(), null, null, 1, default);
        systemAccess.VerifyAll();

        var documentId = Guid.CreateVersion7();
        var missingDocumentId = Guid.CreateVersion7();
        var catalogId = Guid.CreateVersion7();
        var missingCatalogId = Guid.CreateVersion7();
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        documents.Setup(x => x.GetAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentRecord
            {
                Id = documentId,
                TypeCode = "invoice",
                DateUtc = Now,
                Status = DocumentStatus.Draft
            });
        documents.Setup(x => x.GetAsync(missingDocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null);
        var catalogs = new Mock<ICatalogRepository>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetAsync(catalogId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogRecord
            {
                Id = catalogId,
                CatalogCode = "customer",
                IsDeleted = false,
                CreatedAtUtc = Now,
                UpdatedAtUtc = Now
            });
        catalogs.Setup(x => x.GetAsync(missingCatalogId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogRecord?)null);
        var required = new List<(string Kind, string Code, string Action)>();
        var access = new Mock<INgbAccessChecker>(MockBehavior.Strict);
        access.Setup(x => x.HasAsync(
                NgbResourceKinds.System, NgbPermissionResources.Audit, NgbPermissionActions.View,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        access.Setup(x => x.RequireAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((kind, code, action, _) => required.Add((kind, code, action)))
            .Returns(Task.CompletedTask);
        var sut = new PermissionAwareAuditLogQueryService(inner, access.Object, documents.Object, catalogs.Object);

        await sut.GetEntityAuditLogAsync(AuditEntityKind.Document, documentId, null, null, 1, default);
        await sut.GetEntityAuditLogAsync(AuditEntityKind.Document, missingDocumentId, null, null, 1, default);
        await sut.GetEntityAuditLogAsync(AuditEntityKind.Catalog, catalogId, null, null, 1, default);
        await sut.GetEntityAuditLogAsync(AuditEntityKind.Catalog, missingCatalogId, null, null, 1, default);
        await sut.GetEntityAuditLogAsync(AuditEntityKind.ChartOfAccountsAccount, Guid.CreateVersion7(), null, null, 1, default);
        await sut.GetEntityAuditLogAsync(AuditEntityKind.Period, Guid.CreateVersion7(), null, null, 1, default);
        await sut.GetEntityAuditLogAsync(AuditEntityKind.OperationalRegister, Guid.CreateVersion7(), null, null, 1, default);

        required.Should().Equal(
            (NgbResourceKinds.Document, "invoice", NgbPermissionActions.ViewAudit),
            (NgbResourceKinds.System, NgbPermissionResources.Audit, NgbPermissionActions.View),
            (NgbResourceKinds.Catalog, "customer", NgbPermissionActions.ViewAudit),
            (NgbResourceKinds.System, NgbPermissionResources.Audit, NgbPermissionActions.View),
            (NgbResourceKinds.Admin, NgbPermissionResources.ChartOfAccounts, NgbPermissionActions.View),
            (NgbResourceKinds.Admin, NgbPermissionResources.PeriodClosing, NgbPermissionActions.View),
            (NgbResourceKinds.System, NgbPermissionResources.Audit, NgbPermissionActions.View));
        documents.VerifyAll();
        catalogs.VerifyAll();
    }

    [Fact]
    public async Task WriterDisabled_IsStrictNoOpEvenForOtherwiseInvalidInputs()
    {
        var sut = Service(writer: null);

        await sut.WriteAsync(AuditEntityKind.Document, Guid.Empty, " ");
        await sut.WriteBatchAsync(null!);
    }

    [Fact]
    public async Task WriterEnabled_RejectsAllInvalidSingleAndBatchInputs()
    {
        var sut = Service(Mock.Of<IAuditEventWriter>());

        await ((Func<Task>)(() => sut.WriteAsync(AuditEntityKind.Document, Guid.Empty, "action")))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.WriteAsync(AuditEntityKind.Document, Guid.CreateVersion7(), " ")))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.WriteBatchAsync(null!)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await sut.WriteBatchAsync([]);
        await ((Func<Task>)(() => sut.WriteBatchAsync([
                new AuditLogWriteRequest(AuditEntityKind.Document, Guid.Empty, "action")
            ])))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.WriteBatchAsync([
                new AuditLogWriteRequest(AuditEntityKind.Document, Guid.CreateVersion7(), " ")
            ])))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
    }

    [Fact]
    public async Task Writer_MapsAnonymousActorNullMetadataAndNullChanges()
    {
        IReadOnlyList<AuditEvent>? written = null;
        var writer = new Mock<IAuditEventWriter>(MockBehavior.Strict);
        writer.Setup(x => x.WriteBatchAsync(It.IsAny<IReadOnlyList<AuditEvent>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<AuditEvent>, CancellationToken>((events, _) => written = events)
            .Returns(Task.CompletedTask);
        var sut = Service(writer.Object);

        await sut.WriteAsync(AuditEntityKind.Catalog, Guid.CreateVersion7(), " action ");

        written.Should().ContainSingle();
        written![0].ActionCode.Should().Be("action");
        written[0].ActorUserId.Should().BeNull();
        written[0].MetadataJson.Should().BeNull();
        written[0].Changes.Should().BeEmpty();
        written[0].OccurredAtUtc.Should().Be(Now);
    }

    [Fact]
    public async Task Writer_CoversActorWithoutRepositoryAndActorUpsertWithMetadataAndChanges()
    {
        var actor = new ActorIdentity("subject", "user@example.com", "User", false);
        var context = Mock.Of<ICurrentActorContext>(x => x.Current == actor);
        var firstWriter = new Mock<IAuditEventWriter>();
        firstWriter.Setup(x => x.WriteBatchAsync(It.IsAny<IReadOnlyList<AuditEvent>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await Service(firstWriter.Object, context).WriteAsync(
            AuditEntityKind.Period, Guid.CreateVersion7(), "without-users");

        var userId = Guid.CreateVersion7();
        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        users.Setup(x => x.UpsertAsync("subject", "user@example.com", "User", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId);
        IReadOnlyList<AuditEvent>? written = null;
        var writer = new Mock<IAuditEventWriter>(MockBehavior.Strict);
        writer.Setup(x => x.WriteBatchAsync(It.IsAny<IReadOnlyList<AuditEvent>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<AuditEvent>, CancellationToken>((events, _) => written = events)
            .Returns(Task.CompletedTask);
        var change = AuditLogService.Change(" field ", 1, new { Value = 2 });
        var nullChange = AuditLogService.Change("nulls", null, null);
        var requests = new[]
        {
            new AuditLogWriteRequest(
                AuditEntityKind.SecurityUser,
                Guid.CreateVersion7(),
                " updated ",
                [change, nullChange],
                new { Status = AuditEntityKind.SecurityUser },
                Guid.CreateVersion7())
        };

        await Service(writer.Object, context, users.Object).WriteBatchAsync(requests);

        written.Should().ContainSingle();
        written![0].ActorUserId.Should().Be(userId);
        written[0].MetadataJson.Should().Contain("securityUser");
        written[0].Changes.Should().HaveCount(2);
        change.FieldPath.Should().Be("field");
        change.OldValueJson.Should().Be("1");
        change.NewValueJson.Should().Contain("value");
        nullChange.OldValueJson.Should().BeNull();
        nullChange.NewValueJson.Should().BeNull();
        users.VerifyAll();

        Action blankField = () => AuditLogService.Change(" ", null, null);
        blankField.Should().Throw<NgbArgumentRequiredException>();
    }

    private static AuditLogService Service(
        IAuditEventWriter? writer,
        ICurrentActorContext? actor = null,
        IPlatformUserRepository? users = null,
        TimeProvider? clock = null)
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.EnsureActiveTransaction());
        return new AuditLogService(
            uow.Object,
            actor ?? Mock.Of<ICurrentActorContext>(),
            NullLogger<AuditLogService>.Instance,
            clock ?? new FixedTimeProvider(Now),
            users,
            writer);
    }

    private static AuditEvent Event(Guid entityId, Guid? actorId, string action) => new(
        Guid.CreateVersion7(),
        AuditEntityKind.Document,
        entityId,
        action,
        actorId,
        Now,
        Guid.CreateVersion7(),
        "{}",
        [new AuditFieldChange("name", "\"old\"", "\"new\"")]);

    private sealed class FixedTimeProvider(DateTime value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(value);
    }
}
