using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.CashFlow;
using NGB.Contracts.Admin;
using NGB.Contracts.Common;
using NGB.Core.Reporting;
using NGB.Core.Security;
using NGB.Persistence.Accounts;
using NGB.Runtime.Accounts;
using NGB.Runtime.Admin;
using NGB.Runtime.Reporting;
using NGB.Runtime.Security;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Admin;

public sealed class AdminServicesFullCoverageTests
{
    [Fact]
    public async Task AdminService_RejectsEveryInvalidRequestBoundary()
    {
        var sut = Service();

        await ((Func<Task>)(() => sut.GetChartOfAccountsPageAsync(null!, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.GetChartOfAccountsPageAsync(new(Offset: -1), default)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => sut.GetChartOfAccountsPageAsync(new(Limit: 0), default)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => sut.GetChartOfAccountsPageAsync(new(Limit: 501), default)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        sut = Service(coaAdmin: Mock.Of<IChartOfAccountsAdminService>());
        await ((Func<Task>)(() => sut.GetChartOfAccountsPageAsync(new(AccountTypes: [" "]), default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        await ((Func<Task>)(() => sut.CreateChartOfAccountAsync(null!, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.UpdateChartOfAccountAsync(Guid.NewGuid(), null!, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.CreateChartOfAccountAsync(Upsert(accountType: " "), default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.CreateChartOfAccountAsync(Upsert(accountType: "unknown"), default)))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await ((Func<Task>)(() => sut.CreateChartOfAccountAsync(Upsert(cashFlowRole: "unknown"), default)))
            .Should().ThrowAsync<NgbArgumentInvalidException>();

        var cursorKind = SpecializedReportCursorCodec.BuildKind(
            "admin.chart-of-accounts",
            false.ToString(),
            null,
            null,
            string.Empty,
            null,
            string.Empty);
        var negativeOffsetCursor = SpecializedReportCursorCodec.Encode(
            cursorKind,
            new
            {
                Offset = -1,
                Total = 0,
                AfterCode = (string?)null,
                AfterAccountId = (Guid?)null
            });
        await ((Func<Task>)(() => sut.GetChartOfAccountsPageAsync(
                new ChartOfAccountsPageRequestDto(Cursor: negativeOffsetCursor), default)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task AdminService_CoversMenuMetadataFiltersSearchPagingAndLookupShapes()
    {
        var first = Item("2000", "Liability account", AccountType.Liability, CashFlowRole.FinancingCounterparty);
        var second = Item("1000", "Cash", AccountType.Asset);
        var deleted = Item("3000", "Old income", AccountType.Income, deleted: true);
        var menuDto = new MainMenuDto([new MainMenuGroupDto("Main", [], 1)]);
        var menu = new Mock<IMainMenuService>(MockBehavior.Strict);
        menu.Setup(x => x.GetMainMenuAsync(It.IsAny<CancellationToken>())).ReturnsAsync(menuDto);
        var admin = new Mock<IChartOfAccountsAdminService>(MockBehavior.Strict);
        admin.SetupSequence(x => x.GetPageAsync(
                It.IsAny<ChartOfAccountsAdminPageQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChartOfAccountsAdminPage([deleted], 1))
            .ReturnsAsync(new ChartOfAccountsAdminPage([first, second], 2))
            .ReturnsAsync(new ChartOfAccountsAdminPage([first], 1))
            .ReturnsAsync(new ChartOfAccountsAdminPage([second], 1))
            .ReturnsAsync(new ChartOfAccountsAdminPage([second], 1))
            .ReturnsAsync(new ChartOfAccountsAdminPage([first], 2));
        admin.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid accountId, CancellationToken _) =>
                accountId == first.Account.Id ? first : null);
        admin.Setup(x => x.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second]);
        var lines = new Mock<ICashFlowLineRepository>(MockBehavior.Strict);
        lines.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([
            new CashFlowLineDefinition("other", (CashFlowMethod)99, CashFlowSection.Operating, "Other", 1, false),
            new CashFlowLineDefinition("indirect", CashFlowMethod.Indirect, CashFlowSection.Operating, "Indirect", 2, true)
        ]);
        var sut = Service(menu.Object, admin.Object, cashFlowLines: lines.Object);

        (await sut.GetMainMenuAsync(default)).Should().BeSameAs(menuDto);
        var metadata = await sut.GetChartOfAccountsMetadataAsync(default);
        metadata.CashFlowLineOptions.Should().ContainSingle().Which.Value.Should().Be("indirect");

        var deletedPage = await sut.GetChartOfAccountsPageAsync(
            new(IncludeDeleted: true, OnlyDeleted: true), default);
        deletedPage.Items.Should().ContainSingle(x => x.Code == "3000" && x.IsMarkedForDeletion);

        var notDeletedPage = await sut.GetChartOfAccountsPageAsync(
            new(IncludeDeleted: true, OnlyDeleted: false, AccountTypes: []), default);
        notDeletedPage.Items.Should().HaveCount(2);

        var byCode = await sut.GetChartOfAccountsPageAsync(new(Search: "2000"), default);
        var byName = await sut.GetChartOfAccountsPageAsync(new(Search: "cash"), default);
        var byType = await sut.GetChartOfAccountsPageAsync(new(Search: "asset"), default);
        var blankSearch = await sut.GetChartOfAccountsPageAsync(new(Search: "  ", Offset: 1, Limit: 1), default);
        byCode.Items.Should().ContainSingle(x => x.Code == "2000");
        byName.Items.Should().ContainSingle(x => x.Code == "1000");
        byType.Items.Should().ContainSingle(x => x.Code == "1000");
        blankSearch.Items.Should().ContainSingle(x => x.Code == "2000");

        (await sut.GetChartOfAccountAsync(first.Account.Id, default)).CashFlowRole
            .Should().Be(nameof(CashFlowRole.FinancingCounterparty));
        await ((Func<Task>)(() => sut.GetChartOfAccountAsync(Guid.NewGuid(), default)))
            .Should().ThrowAsync<AccountNotFoundException>();
        await ((Func<Task>)(() => sut.GetChartOfAccountsByIdsAsync(null!, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        (await sut.GetChartOfAccountsByIdsAsync([], default)).Should().BeEmpty();
        (await sut.GetChartOfAccountsByIdsAsync([Guid.Empty, Guid.Empty], default)).Should().BeEmpty();
        var tooManyIds = Enumerable.Range(0, PagingLimits.MaxLookupIds + 1)
            .Select(_ => Guid.NewGuid())
            .ToArray();
        await ((Func<Task>)(() => sut.GetChartOfAccountsByIdsAsync(tooManyIds, default)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        var lookup = await sut.GetChartOfAccountsByIdsAsync(
            [first.Account.Id, Guid.Empty, first.Account.Id, second.Account.Id], default);
        lookup.Select(x => x.Label).Should().Equal("2000 — Liability account", "1000 — Cash");
    }

    [Fact]
    public async Task AdminService_ChartOfAccountsCursorCarriesTotalAndRejectsChangedFilters()
    {
        var first = Item("1000", "Cash", AccountType.Asset);
        var second = Item("1100", "Bank", AccountType.Asset);
        var admin = new Mock<IChartOfAccountsAdminService>(MockBehavior.Strict);
        admin.Setup(x => x.GetPageAsync(
                It.Is<ChartOfAccountsAdminPageQuery>(query =>
                    query.Offset == 0 && query.Limit == 1 && query.KnownTotal == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChartOfAccountsAdminPage([first], 2));
        admin.Setup(x => x.GetPageAsync(
                It.Is<ChartOfAccountsAdminPageQuery>(query =>
                    query.Offset == 1 && query.Limit == 1 && query.KnownTotal == 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChartOfAccountsAdminPage([second], 2));
        var sut = Service(coaAdmin: admin.Object);
        var request = new ChartOfAccountsPageRequestDto(Limit: 1, AccountTypes: ["Asset"]);

        var firstPage = await sut.GetChartOfAccountsPageAsync(request, default);
        var secondPage = await sut.GetChartOfAccountsPageAsync(
            request with { Cursor = firstPage.NextCursor },
            default);

        firstPage.NextCursor.Should().NotBeNullOrWhiteSpace();
        secondPage.Items.Should().ContainSingle().Which.Code.Should().Be("1100");
        secondPage.NextCursor.Should().BeNull();
        Func<Task> changedFilter = () => sut.GetChartOfAccountsPageAsync(
            request with { Cursor = firstPage.NextCursor, OnlyActive = true },
            default);
        await changedFilter.Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Fact]
    public async Task AdminService_CoversCreateUpdateAndAllManagementDelegations()
    {
        var id = Guid.NewGuid();
        var item = Item("1000", "Cash", AccountType.Asset);
        item = new ChartOfAccountsAdminItem { Account = new Account(id, "1000", "Cash", AccountType.Asset), IsActive = true };
        var admin = new Mock<IChartOfAccountsAdminService>(MockBehavior.Strict);
        admin.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        var management = new Mock<IChartOfAccountsManagementService>(MockBehavior.Strict);
        management.Setup(x => x.CreateAsync(
                It.Is<CreateAccountRequest>(r => r.Type == AccountType.Asset && r.CashFlowRole == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(id);
        management.Setup(x => x.UpdateAsync(
                It.Is<UpdateAccountRequest>(r => r.AccountId == id
                    && r.Type == AccountType.Asset
                    && r.CashFlowRole == CashFlowRole.CashEquivalent),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        management.Setup(x => x.MarkForDeletionAsync(id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        management.Setup(x => x.UnmarkForDeletionAsync(id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        management.Setup(x => x.SetActiveAsync(id, false, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var sut = Service(coaAdmin: admin.Object, management: management.Object);

        (await sut.CreateChartOfAccountAsync(Upsert(cashFlowRole: " "), default)).AccountId.Should().Be(id);
        (await sut.UpdateChartOfAccountAsync(id, Upsert(cashFlowRole: "cashequivalent"), default)).AccountId.Should().Be(id);
        await sut.MarkChartOfAccountForDeletionAsync(id, default);
        await sut.UnmarkChartOfAccountForDeletionAsync(id, default);
        await sut.SetChartOfAccountActiveAsync(id, false, default);

        management.VerifyAll();
    }

    [Fact]
    public async Task PermissionAwareMenu_CoversAnonymousInactiveEmptyAndEveryResourceKind()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = Cache(memory);
        var access = new Mock<INgbAccessChecker>(MockBehavior.Strict);
        access.SetupSequence(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionSnapshot.Anonymous)
            .ReturnsAsync(Snapshot(isActive: false, accessVersion: 1))
            .ReturnsAsync(Snapshot(accessVersion: 2))
            .ReturnsAsync(Snapshot(accessVersion: 3, permissions: Permissions()));
        var menu = new Mock<IMainMenuService>(MockBehavior.Strict);
        menu.SetupSequence(x => x.GetMainMenuAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MainMenuDto([]))
            .ReturnsAsync(MenuWithEveryKind());
        var sut = new PermissionAwareAdminService(Service(menu.Object), access.Object, cache);

        (await sut.GetMainMenuAsync(default)).Groups.Should().BeEmpty();
        (await sut.GetMainMenuAsync(default)).Groups.Should().BeEmpty();
        (await sut.GetMainMenuAsync(default)).Groups.Should().BeEmpty();
        var filtered = await sut.GetMainMenuAsync(default);

        filtered.Groups.Should().ContainSingle();
        filtered.Groups[0].Items.Select(x => x.Label).Should().Equal(
            "invoice", "customer", "balance", "cash", "coa-code", "coa-route", "period-code", "period-route",
            "posting-code", "posting-route", "integrity-code", "integrity-route", "custom", "users", "roles",
            "report-page", "dashboard", "external");
    }

    [Fact]
    public async Task PermissionAwareAdmin_CoversAllViewAndManageWrappers()
    {
        var id = Guid.NewGuid();
        var item = new ChartOfAccountsAdminItem
        {
            Account = new Account(id, "1000", "Cash", AccountType.Asset),
            IsActive = true
        };
        var menu = new Mock<IMainMenuService>();
        menu.Setup(x => x.GetMainMenuAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new MainMenuDto([]));
        var admin = new Mock<IChartOfAccountsAdminService>();
        admin.Setup(x => x.GetPageAsync(
                It.IsAny<ChartOfAccountsAdminPageQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChartOfAccountsAdminPage([item], 1));
        admin.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        admin.Setup(x => x.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([item]);
        var management = new Mock<IChartOfAccountsManagementService>();
        management.Setup(x => x.CreateAsync(It.IsAny<CreateAccountRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(id);
        var lines = new Mock<ICashFlowLineRepository>();
        lines.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var inner = Service(menu.Object, admin.Object, management.Object, lines.Object);
        var access = new Mock<INgbAccessChecker>(MockBehavior.Strict);
        access.Setup(x => x.RequireAsync(
                NgbResourceKinds.Admin, NgbPermissionResources.ChartOfAccounts, It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var sut = new PermissionAwareAdminService(inner, access.Object, Cache(memory));

        await sut.GetChartOfAccountsMetadataAsync(default);
        await sut.GetChartOfAccountsPageAsync(new(), default);
        await sut.GetChartOfAccountAsync(id, default);
        await sut.GetChartOfAccountsByIdsAsync([id], default);
        await sut.CreateChartOfAccountAsync(Upsert(), default);
        await sut.UpdateChartOfAccountAsync(id, Upsert(), default);
        await sut.MarkChartOfAccountForDeletionAsync(id, default);
        await sut.UnmarkChartOfAccountForDeletionAsync(id, default);
        await sut.SetChartOfAccountActiveAsync(id, true, default);

        access.Verify(x => x.RequireAsync(
            NgbResourceKinds.Admin, NgbPermissionResources.ChartOfAccounts, NgbPermissionActions.View,
            It.IsAny<CancellationToken>()), Times.Exactly(4));
        access.Verify(x => x.RequireAsync(
            NgbResourceKinds.Admin, NgbPermissionResources.ChartOfAccounts, NgbPermissionActions.Manage,
            It.IsAny<CancellationToken>()), Times.Exactly(5));
    }

    private static AdminService Service(
        IMainMenuService? menu = null,
        IChartOfAccountsAdminService? coaAdmin = null,
        IChartOfAccountsManagementService? management = null,
        ICashFlowLineRepository? cashFlowLines = null)
        => new(
            menu ?? Mock.Of<IMainMenuService>(),
            coaAdmin ?? Mock.Of<IChartOfAccountsAdminService>(),
            management ?? Mock.Of<IChartOfAccountsManagementService>(),
            cashFlowLines ?? Mock.Of<ICashFlowLineRepository>());

    private static ChartOfAccountsUpsertRequestDto Upsert(
        string accountType = "Asset",
        string? cashFlowRole = null)
        => new("1000", "Cash", accountType, true, cashFlowRole);

    private static ChartOfAccountsAdminItem Item(
        string code,
        string name,
        AccountType type,
        CashFlowRole role = CashFlowRole.None,
        bool deleted = false)
        => new()
        {
            Account = new Account(Guid.NewGuid(), code, name, type, cashFlowRole: role),
            IsActive = !deleted,
            IsDeleted = deleted
        };

    private static NgbSecurityCache Cache(IMemoryCache memory)
        => new(memory, new OptionsMonitor(new NgbSecurityCacheOptions()));

    private static PermissionSnapshot Snapshot(
        bool isActive = true,
        long accessVersion = 0,
        IReadOnlyCollection<NgbPermissionKey>? permissions = null)
        => new(
            Guid.NewGuid(),
            "subject",
            isAuthenticated: true,
            isActive,
            isBootstrapAdmin: false,
            accessVersion,
            permissions ?? []);

    private static IReadOnlyCollection<NgbPermissionKey> Permissions() =>
    [
        Key(NgbResourceKinds.Document, "invoice", NgbPermissionActions.View),
        Key(NgbResourceKinds.Catalog, "customer", NgbPermissionActions.View),
        Key(NgbResourceKinds.Report, "balance", NgbPermissionActions.View),
        Key(NgbResourceKinds.Report, "cash", NgbPermissionActions.Execute),
        Key(NgbResourceKinds.Admin, NgbPermissionResources.ChartOfAccounts, NgbPermissionActions.View),
        Key(NgbResourceKinds.Admin, NgbPermissionResources.PeriodClosing, NgbPermissionActions.View),
        Key(NgbResourceKinds.Admin, NgbPermissionResources.PostingLog, NgbPermissionActions.View),
        Key(NgbResourceKinds.Admin, NgbPermissionResources.Integrity, NgbPermissionActions.View),
        Key(NgbResourceKinds.Admin, "custom", NgbPermissionActions.View),
        Key(NgbResourceKinds.System, NgbPermissionResources.Users, NgbPermissionActions.View),
        Key(NgbResourceKinds.System, NgbPermissionResources.Roles, NgbPermissionActions.View),
        Key(NgbResourceKinds.Page, "dashboard", NgbPermissionActions.View),
        Key(NgbResourceKinds.External, "external", NgbPermissionActions.View)
    ];

    private static NgbPermissionKey Key(string kind, string code, string action) => new(kind, code, action);

    private static MainMenuDto MenuWithEveryKind()
    {
        var items = new List<MainMenuItemDto>
        {
            MenuItem(" Document ", "invoice", "/documents/invoice"),
            MenuItem("document", "denied-document", "/documents/denied"),
            MenuItem("catalog", "customer", "/catalogs/customer"),
            MenuItem("report", "balance", "/reports/balance"),
            MenuItem("report", "cash", "/reports/cash"),
            MenuItem("admin", NgbPermissionResources.ChartOfAccounts, "/other", label: "coa-code"),
            MenuItem("admin", "coa-route", "/admin/chart-of-accounts", label: "coa-route"),
            MenuItem("admin", "accounting.period_closing", "/other", label: "period-code"),
            MenuItem("admin", "period-route", "/admin/accounting/period-closing", label: "period-route"),
            MenuItem("admin", AccountingReportCodes.PostingLog, "/other", label: "posting-code"),
            MenuItem("admin", "posting-route", "/admin/accounting/posting-log", label: "posting-route"),
            MenuItem("admin", AccountingReportCodes.Consistency, "/other", label: "integrity-code"),
            MenuItem("admin", "integrity-route", "/admin/accounting/consistency", label: "integrity-route"),
            MenuItem("admin", "custom", "/admin/custom"),
            MenuItem("page", "users", "/admin/security/users"),
            MenuItem("page", "roles", "/admin/security/roles"),
            MenuItem("page", "cash", "/reports/cash", label: "report-page"),
            MenuItem("page", "dashboard", "/dashboard"),
            MenuItem("external", "external", "https://example.test"),
            MenuItem("unknown", "unknown", "/unknown")
        };

        return new MainMenuDto([
            new MainMenuGroupDto("Allowed", items),
            new MainMenuGroupDto("Denied", [MenuItem("document", "denied-only", "/denied")])
        ]);
    }

    private static MainMenuItemDto MenuItem(
        string kind,
        string code,
        string route,
        string? label = null)
        => new(kind, code, label ?? code, route);

    private sealed class OptionsMonitor(NgbSecurityCacheOptions value) : IOptionsMonitor<NgbSecurityCacheOptions>
    {
        public NgbSecurityCacheOptions CurrentValue { get; } = value;
        public NgbSecurityCacheOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<NgbSecurityCacheOptions, string?> listener) => null;
    }
}
