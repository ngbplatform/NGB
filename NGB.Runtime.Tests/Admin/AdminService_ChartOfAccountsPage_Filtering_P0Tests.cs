using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.CashFlow;
using NGB.Contracts.Admin;
using NGB.Persistence.Accounts;
using NGB.Runtime.Accounts;
using NGB.Runtime.Admin;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Admin;

public sealed class AdminService_ChartOfAccountsPage_Filtering_P0Tests
{
    [Fact]
    public async Task GetPage_Passes_IncludeDeleted_To_AdminReader()
    {
        var menu = StubMenu();

        var coaAdmin = new Mock<IChartOfAccountsAdminService>(MockBehavior.Strict);
        coaAdmin
            .Setup(x => x.GetAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .Verifiable();

        var coaMgmt = new Mock<IChartOfAccountsManagementService>(MockBehavior.Strict);
        var cashFlowLines = StubCashFlowLines();
        var svc = new AdminService(menu, coaAdmin.Object, coaMgmt.Object, cashFlowLines.Object);

        await svc.GetChartOfAccountsPageAsync(new ChartOfAccountsPageRequestDto(IncludeDeleted: false), CancellationToken.None);

        coaAdmin.Verify();
    }

    [Fact]
    public async Task GetPage_Filters_By_OnlyActive_WhenSet()
    {
        var menu = StubMenu();
        var items = new[]
        {
            Item("1000", AccountType.Asset, isActive: true, isDeleted: false),
            Item("2000", AccountType.Liability, isActive: false, isDeleted: false),
            Item("3000", AccountType.Income, isActive: true, isDeleted: false),
        };

        var coaAdmin = new Mock<IChartOfAccountsAdminService>(MockBehavior.Strict);
        coaAdmin
            .Setup(x => x.GetAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items)
            .Verifiable();

        var coaMgmt = new Mock<IChartOfAccountsManagementService>(MockBehavior.Strict);
        var cashFlowLines = StubCashFlowLines();
        var svc = new AdminService(menu, coaAdmin.Object, coaMgmt.Object, cashFlowLines.Object);

        var page = await svc.GetChartOfAccountsPageAsync(
            new ChartOfAccountsPageRequestDto(OnlyActive: true),
            CancellationToken.None);

        page.Items.Select(x => x.Code).Should().BeEquivalentTo(["1000", "3000"]);
        page.Total.Should().Be(2);
    }

    [Fact]
    public async Task GetPage_Filters_By_OnlyInactive_WhenOnlyActiveIsFalse()
    {
        var menu = StubMenu();
        var items = new[]
        {
            Item("1000", AccountType.Asset, isActive: true, isDeleted: false),
            Item("2000", AccountType.Liability, isActive: false, isDeleted: false),
        };

        var coaAdmin = new Mock<IChartOfAccountsAdminService>(MockBehavior.Strict);
        coaAdmin
            .Setup(x => x.GetAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items)
            .Verifiable();

        var coaMgmt = new Mock<IChartOfAccountsManagementService>(MockBehavior.Strict);
        var cashFlowLines = StubCashFlowLines();
        var svc = new AdminService(menu, coaAdmin.Object, coaMgmt.Object, cashFlowLines.Object);

        var page = await svc.GetChartOfAccountsPageAsync(
            new ChartOfAccountsPageRequestDto(OnlyActive: false),
            CancellationToken.None);

        page.Items.Select(x => x.Code).Should().BeEquivalentTo(["2000"]);
        page.Total.Should().Be(1);
    }

    [Fact]
    public async Task GetPage_Filters_By_AccountTypes_And_Search()
    {
        var menu = StubMenu();
        var items = new[]
        {
            Item("1000", AccountType.Asset, isActive: true, isDeleted: false, name: "Cash"),
            Item("2000", AccountType.Liability, isActive: true, isDeleted: false, name: "Accounts Payable"),
            Item("4000", AccountType.Income, isActive: true, isDeleted: false, name: "Rent Income"),
        };

        var coaAdmin = new Mock<IChartOfAccountsAdminService>(MockBehavior.Strict);
        coaAdmin
            .Setup(x => x.GetAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items)
            .Verifiable();

        var coaMgmt = new Mock<IChartOfAccountsManagementService>(MockBehavior.Strict);
        var cashFlowLines = StubCashFlowLines();
        var svc = new AdminService(menu, coaAdmin.Object, coaMgmt.Object, cashFlowLines.Object);

        var page = await svc.GetChartOfAccountsPageAsync(
            new ChartOfAccountsPageRequestDto(
                Search: "rent",
                AccountTypes: ["Income"]),
            CancellationToken.None);

        page.Items.Should().HaveCount(1);
        page.Items[0].Code.Should().Be("4000");
    }

    [Fact]
    public async Task GetPage_AccountTypes_InvalidValue_Throws()
    {
        var menu = StubMenu();

        var coaAdmin = new Mock<IChartOfAccountsAdminService>(MockBehavior.Strict);
        coaAdmin
            .Setup(x => x.GetAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var coaMgmt = new Mock<IChartOfAccountsManagementService>(MockBehavior.Strict);
        var cashFlowLines = StubCashFlowLines();
        var svc = new AdminService(menu, coaAdmin.Object, coaMgmt.Object, cashFlowLines.Object);

        var act = () => svc.GetChartOfAccountsPageAsync(
            new ChartOfAccountsPageRequestDto(AccountTypes: ["nope"]),
            CancellationToken.None);

        await act.Should().ThrowAsync<NgbArgumentInvalidException>()
            .Where(x => x.ParamName == nameof(ChartOfAccountsPageRequestDto.AccountTypes));
    }

    [Fact]
    public async Task GetChartOfAccountsMetadata_Returns_BackendOwned_Options_And_RoleCapabilities()
    {
        var menu = StubMenu();
        var coaAdmin = new Mock<IChartOfAccountsAdminService>(MockBehavior.Strict);
        var coaMgmt = new Mock<IChartOfAccountsManagementService>(MockBehavior.Strict);
        var cashFlowLines = StubCashFlowLines(
            new CashFlowLineDefinition(
                CashFlowSystemLineCodes.WorkingCapitalAccountsReceivable,
                CashFlowMethod.Indirect,
                CashFlowSection.Operating,
                "Change in Accounts Receivable",
                10,
                true),
            new CashFlowLineDefinition(
                CashFlowSystemLineCodes.FinancingDebtNet,
                CashFlowMethod.Indirect,
                CashFlowSection.Financing,
                "Net debt proceeds (repayments)",
                40,
                true));

        var svc = new AdminService(menu, coaAdmin.Object, coaMgmt.Object, cashFlowLines.Object);

        var metadata = await svc.GetChartOfAccountsMetadataAsync(CancellationToken.None);

        metadata.AccountTypeOptions.Select(x => x.Value)
            .Should().Contain(["Asset", "Liability", "Equity", "Income", "Expense"]);

        metadata.CashFlowRoleOptions.Should().ContainEquivalentOf(new ChartOfAccountsCashFlowRoleOptionDto(
            Value: string.Empty,
            Label: "None",
            SupportsLineCode: false,
            RequiresLineCode: false));

        metadata.CashFlowRoleOptions.Should().ContainEquivalentOf(new ChartOfAccountsCashFlowRoleOptionDto(
            Value: nameof(CashFlowRole.WorkingCapital),
            Label: "Working capital",
            SupportsLineCode: true,
            RequiresLineCode: true));

        metadata.CashFlowLineOptions.Should().ContainEquivalentOf(new ChartOfAccountsCashFlowLineOptionDto(
            Value: CashFlowSystemLineCodes.WorkingCapitalAccountsReceivable,
            Label: "Change in Accounts Receivable",
            Section: nameof(CashFlowSection.Operating),
            AllowedRoles: [nameof(CashFlowRole.WorkingCapital)]));

        metadata.CashFlowLineOptions.Should().ContainEquivalentOf(new ChartOfAccountsCashFlowLineOptionDto(
            Value: CashFlowSystemLineCodes.FinancingDebtNet,
            Label: "Net debt proceeds (repayments)",
            Section: nameof(CashFlowSection.Financing),
            AllowedRoles: [nameof(CashFlowRole.FinancingCounterparty)]));
    }

    private static IMainMenuService StubMenu()
    {
        var menu = new Mock<IMainMenuService>(MockBehavior.Strict);
        menu.Setup(x => x.GetMainMenuAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MainMenuDto([]));
        return menu.Object;
    }

    private static Mock<ICashFlowLineRepository> StubCashFlowLines(params CashFlowLineDefinition[] lines)
    {
        var repo = new Mock<ICashFlowLineRepository>(MockBehavior.Strict);
        repo.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(lines);
        return repo;
    }

    private static ChartOfAccountsAdminItem Item(
        string code,
        AccountType type,
        bool isActive,
        bool isDeleted,
        string? name = null)
        => new()
        {
            Account = new Account(
                id: Guid.CreateVersion7(),
                code: code,
                name: name ?? code,
                type: type),
            IsActive = isActive,
            IsDeleted = isDeleted
        };
}
