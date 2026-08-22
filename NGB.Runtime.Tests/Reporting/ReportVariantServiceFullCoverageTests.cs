using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.AuditLog;
using NGB.Core.Reporting.Exceptions;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Reporting;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Definitions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportVariantServiceFullCoverageTests
{
    [Fact]
    public void Constructor_RejectsEveryMissingRequiredDependency()
    {
        var repository = Mock.Of<IReportVariantRepository>();
        var definitions = Mock.Of<IReportDefinitionProvider>();
        var access = Mock.Of<IReportVariantAccessContext>();

        ((Action)(() => _ = new ReportVariantService(null!, definitions, access)))
            .Should().Throw<NgbArgumentRequiredException>().Which.ParamName.Should().Be("repository");
        ((Action)(() => _ = new ReportVariantService(repository, null!, access)))
            .Should().Throw<NgbArgumentRequiredException>().Which.ParamName.Should().Be("definitions");
        ((Action)(() => _ = new ReportVariantService(repository, definitions, null!)))
            .Should().Throw<NgbArgumentRequiredException>().Which.ParamName.Should().Be("accessContext");
    }

    [Fact]
    public async Task GetAsync_RejectsBlankCode_AndReturnsNullOrMappedVariant()
    {
        var fixture = new Fixture(authSubject: "  actor-1  ", includePlatformUsers: false);

        var blankAct = async () => await fixture.Sut.GetAsync("report", "  ", default);
        await blankAct.Should().ThrowAsync<NgbArgumentRequiredException>();

        fixture.Repository
            .Setup(repository => repository.GetVisibleAsync("accounting.ledger.analysis", "missing", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReportVariantRecord?)null);
        (await fixture.Sut.GetAsync("ignored", " Missing ", default)).Should().BeNull();

        var record = Record(
            variantCode: "Mapped",
            isShared: true,
            layoutJson: "{\"showGrandTotals\":true}",
            filtersJson: "{\"account_id\":{\"value\":\"11111111-1111-1111-1111-111111111111\"}}",
            parametersJson: "{\"from_utc\":\"2026-01-01\"}");
        fixture.Repository
            .Setup(repository => repository.GetVisibleAsync("accounting.ledger.analysis", "mapped", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var mapped = await fixture.Sut.GetAsync("ignored", "MAPPED", default);

        mapped.Should().NotBeNull();
        mapped!.VariantCode.Should().Be("Mapped");
        mapped.Layout!.ShowGrandTotals.Should().BeTrue();
        mapped.Filters!["account_id"].Value.GetGuid().Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        mapped.Parameters!["from_utc"].Should().Be("2026-01-01");
    }

    [Fact]
    public async Task SaveAsync_RejectsNullAndBlankName()
    {
        var fixture = new Fixture();

        var nullAct = async () => await fixture.Sut.SaveAsync(null!, default);
        await nullAct.Should().ThrowAsync<NgbArgumentRequiredException>();

        var blankNameAct = async () => await fixture.Sut.SaveAsync(Variant(name: " \t "), default);
        var error = await blankNameAct.Should().ThrowAsync<ReportVariantValidationException>();
        error.Which.Context["reason"].Should().Be("name_required");
    }

    [Fact]
    public async Task SaveAsync_PrivateVariantWithActor_RequiresPlatformProjectionSupport()
    {
        var fixture = new Fixture(authSubject: "actor-1", includePlatformUsers: false);

        var act = async () => await fixture.Sut.SaveAsync(Variant(isShared: false), default);

        var error = await act.Should().ThrowAsync<ReportVariantValidationException>();
        error.Which.Context["reason"].Should().Be("owner_platform_user_unavailable");
    }

    [Fact]
    public async Task SaveAsync_SharedVariantConflictsWithExistingPrivateVariant()
    {
        var fixture = new Fixture(
            authSubject: "actor-1",
            platformUserId: Guid.Parse("44444444-4444-4444-4444-444444444444"));
        fixture.Repository
            .Setup(repository => repository.ListByCodeAsync("accounting.ledger.analysis", "view", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Record(
                isShared: false,
                ownerPlatformUserId: null)]);

        var act = async () => await fixture.Sut.SaveAsync(Variant(isShared: true), default);

        await act.Should().ThrowAsync<ReportVariantCodeConflictException>();
    }

    [Fact]
    public async Task SaveAsync_ExistingSharedVariant_PreservesIdentitySerializesPayloadAndCommitsTransaction()
    {
        var now = new DateTime(2026, 8, 21, 12, 34, 56, DateTimeKind.Utc);
        var ownerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var existing = Record(isShared: true, ownerPlatformUserId: ownerId, createdAtUtc: now.AddDays(-10));
        var fixture = new Fixture(
            authSubject: "actor-1",
            platformUserId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            uow: TransactionalUow(hasActiveTransaction: false),
            timeProvider: new FixedTimeProvider(now));
        fixture.Repository
            .Setup(repository => repository.ListByCodeAsync("accounting.ledger.analysis", "view", It.IsAny<CancellationToken>()))
            .ReturnsAsync([existing]);
        ReportVariantRecord? savedRecord = null;
        fixture.Repository
            .Setup(repository => repository.UpsertAsync(It.IsAny<ReportVariantRecord>(), It.IsAny<CancellationToken>()))
            .Callback<ReportVariantRecord, CancellationToken>((record, _) => savedRecord = record)
            .ReturnsAsync((ReportVariantRecord record, CancellationToken _) => record);
        var variant = Variant(
            variantCode: "  View  ",
            name: "  Updated view  ",
            isShared: true,
            isDefault: true,
            layout: new ReportLayoutDto(ShowGrandTotals: true),
            filters: new Dictionary<string, ReportFilterValueDto>
            {
                ["account_id"] = new(JsonSerializer.SerializeToElement(ownerId))
            },
            parameters: new Dictionary<string, string> { ["from_utc"] = "2026-01-01" });

        var result = await fixture.Sut.SaveAsync(variant, default);

        result.Name.Should().Be("Updated view");
        savedRecord.Should().NotBeNull();
        savedRecord!.ReportVariantId.Should().Be(existing.ReportVariantId);
        savedRecord.OwnerPlatformUserId.Should().Be(ownerId);
        savedRecord.CreatedAtUtc.Should().Be(existing.CreatedAtUtc);
        savedRecord.UpdatedAtUtc.Should().Be(now);
        savedRecord.VariantCode.Should().Be("View");
        savedRecord.LayoutJson.Should().Contain("showGrandTotals");
        savedRecord.FiltersJson.Should().Contain("account_id");
        savedRecord.ParametersJson.Should().Contain("from_utc");
        fixture.Repository.Verify(repository => repository.ClearDefaultAsync(
            "accounting.ledger.analysis", null, true, "view", It.IsAny<CancellationToken>()), Times.Once);
        fixture.Uow!.Verify(unit => unit.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.Uow.Verify(unit => unit.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.Uow.Verify(unit => unit.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveAsync_ExistingPrivateVariant_UsesOwnerScopeInsideActiveTransaction()
    {
        var ownerId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var existing = Record(isShared: false, ownerPlatformUserId: ownerId);
        var fixture = new Fixture(
            authSubject: "actor-1",
            platformUserId: ownerId,
            uow: TransactionalUow(hasActiveTransaction: true));
        fixture.Repository
            .Setup(repository => repository.ListByCodeAsync("accounting.ledger.analysis", "view", It.IsAny<CancellationToken>()))
            .ReturnsAsync([existing]);

        var result = await fixture.Sut.SaveAsync(Variant(isShared: false, isDefault: true), default);

        result.IsShared.Should().BeFalse();
        fixture.Repository.Verify(repository => repository.ClearDefaultAsync(
            "accounting.ledger.analysis", ownerId, false, "view", It.IsAny<CancellationToken>()), Times.Once);
        fixture.Uow!.Verify(unit => unit.EnsureActiveTransaction(), Times.Once);
        fixture.Uow.Verify(unit => unit.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.Uow.Verify(unit => unit.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_CoversValidationSuccessNotFoundAndActiveTransaction()
    {
        var fixture = new Fixture();
        var blankAct = async () => await fixture.Sut.DeleteAsync("report", " ", default);
        await blankAct.Should().ThrowAsync<NgbArgumentRequiredException>();

        fixture.Repository
            .SetupSequence(repository => repository.DeleteVisibleAsync(
                "accounting.ledger.analysis", "view", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);
        await fixture.Sut.DeleteAsync("ignored", " View ", default);
        var missingAct = async () => await fixture.Sut.DeleteAsync("ignored", "view", default);
        await missingAct.Should().ThrowAsync<ReportVariantNotFoundException>();

        var transactional = new Fixture(uow: TransactionalUow(hasActiveTransaction: true));
        transactional.Repository
            .Setup(repository => repository.DeleteVisibleAsync(
                "accounting.ledger.analysis", "view", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        await transactional.Sut.DeleteAsync("ignored", "view", default);
        transactional.Uow!.Verify(unit => unit.EnsureActiveTransaction(), Times.Once);
    }

    private static ReportVariantDto Variant(
        string variantCode = "view",
        string name = "View",
        bool isShared = true,
        bool isDefault = false,
        ReportLayoutDto? layout = null,
        IReadOnlyDictionary<string, ReportFilterValueDto>? filters = null,
        IReadOnlyDictionary<string, string>? parameters = null)
        => new(
            variantCode,
            "ignored.report",
            name,
            layout,
            filters,
            parameters,
            isDefault,
            isShared);

    private static ReportVariantRecord Record(
        string variantCode = "view",
        bool isShared = true,
        Guid? ownerPlatformUserId = null,
        string? layoutJson = null,
        string? filtersJson = null,
        string? parametersJson = null,
        DateTime? createdAtUtc = null)
        => new(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "accounting.ledger.analysis",
            "accounting.ledger.analysis",
            variantCode,
            variantCode.Trim().ToLowerInvariant(),
            ownerPlatformUserId,
            "Stored view",
            layoutJson,
            filtersJson,
            parametersJson,
            IsDefault: false,
            IsShared: isShared,
            CreatedAtUtc: createdAtUtc ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

    private static Mock<IUnitOfWork> TransactionalUow(bool hasActiveTransaction)
    {
        var uow = new Mock<IUnitOfWork>(MockBehavior.Loose);
        uow.SetupGet(unit => unit.HasActiveTransaction).Returns(hasActiveTransaction);
        uow.Setup(unit => unit.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(unit => unit.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(unit => unit.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return uow;
    }

    private sealed class Fixture
    {
        public Fixture(
            string? authSubject = null,
            bool includePlatformUsers = true,
            Guid? platformUserId = null,
            Mock<IUnitOfWork>? uow = null,
            TimeProvider? timeProvider = null)
        {
            Repository = new Mock<IReportVariantRepository>(MockBehavior.Loose);
            Repository
                .Setup(repository => repository.ListVisibleAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            Repository
                .Setup(repository => repository.ListByCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            Repository
                .Setup(repository => repository.UpsertAsync(It.IsAny<ReportVariantRecord>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ReportVariantRecord record, CancellationToken _) => record);
            Repository
                .Setup(repository => repository.ClearDefaultAsync(
                    It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var definition = new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single();
            var definitions = new Mock<IReportDefinitionProvider>();
            definitions
                .Setup(provider => provider.GetDefinitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(definition);

            var access = new Mock<IReportVariantAccessContext>();
            access.SetupGet(context => context.AuthSubject).Returns(authSubject);
            access.SetupGet(context => context.Email).Returns(authSubject is null ? null : $" {authSubject.Trim()}@example.test ");
            access.SetupGet(context => context.DisplayName).Returns(authSubject is null ? null : $" {authSubject.Trim()} ");
            access.SetupGet(context => context.IsActive).Returns(authSubject is not null);

            Mock<IPlatformUserRepository>? users = null;
            if (includePlatformUsers)
            {
                var userId = platformUserId ?? Guid.Parse("99999999-9999-9999-9999-999999999999");
                users = new Mock<IPlatformUserRepository>();
                users
                    .Setup(repository => repository.UpsertAsync(
                        It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(userId);
                users
                    .Setup(repository => repository.GetByAuthSubjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((PlatformUser?)null);
            }

            Uow = uow;
            Sut = new ReportVariantService(
                Repository.Object,
                definitions.Object,
                access.Object,
                users?.Object,
                uow?.Object,
                timeProvider);
        }

        public Mock<IReportVariantRepository> Repository { get; }
        public Mock<IUnitOfWork>? Uow { get; }
        public ReportVariantService Sut { get; }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
