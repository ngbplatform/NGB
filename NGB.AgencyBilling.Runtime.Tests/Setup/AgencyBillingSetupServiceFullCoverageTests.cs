using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.CashFlow;
using NGB.Accounting.Dimensions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Runtime.Accounts;
using NGB.Runtime.Accounts.Exceptions;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Tests.Setup;

public sealed class AgencyBillingSetupServiceFullCoverageTests
{
    [Fact]
    public async Task EnsureDefaultsAsync_FirstRunCreatesEveryDefault()
    {
        var state = new SetupState();
        var harness = CreateHarness(state);

        var result = await harness.Service.EnsureDefaultsAsync();

        result.CreatedCashAccount.Should().BeTrue();
        result.CreatedAccountsReceivableAccount.Should().BeTrue();
        result.CreatedServiceRevenueAccount.Should().BeTrue();
        result.CreatedProjectTimeLedgerOperationalRegister.Should().BeTrue();
        result.CreatedUnbilledTimeOperationalRegister.Should().BeTrue();
        result.CreatedProjectBillingStatusOperationalRegister.Should().BeTrue();
        result.CreatedArOpenItemsOperationalRegister.Should().BeTrue();
        result.CreatedAccountingPolicy.Should().BeTrue();
        state.CreatedAccounts.Select(request => request.Code).Should().Equal("1000", "1100", "4000");
        state.UpsertedRegisterCodes.Should().HaveCount(4);
        state.ReplacedResources.Should().HaveCount(4);
        state.ReplacedDimensionRules.Should().HaveCount(4);
        state.EnsuredRegisterIds.Should().HaveCount(4);
        state.CreatedCatalogTypes.Should().ContainSingle(type => type == AgencyBillingCodes.AccountingPolicy);
        state.CreatedCatalogTypes.Count(type => type == AgencyBillingCodes.PaymentTerms).Should().Be(3);
    }

    [Fact]
    public async Task EnsureDefaultsAsync_ExistingCompatibleStateIsIdempotentAndUpdatesCatalogDefaults()
    {
        var state = new SetupState
        {
            Accounts = ValidAccounts(),
            RegistersExist = true,
            PolicyItems = [CatalogItem("Existing Policy")],
            PaymentTermsItems =
            [
                CatalogItem("due on receipt"),
                CatalogItem("NET 15"),
                CatalogItem("Net 30")
            ]
        };
        var harness = CreateHarness(state);

        var result = await harness.Service.EnsureDefaultsAsync();

        result.CreatedCashAccount.Should().BeFalse();
        result.CreatedAccountsReceivableAccount.Should().BeFalse();
        result.CreatedServiceRevenueAccount.Should().BeFalse();
        result.CreatedProjectTimeLedgerOperationalRegister.Should().BeFalse();
        result.CreatedUnbilledTimeOperationalRegister.Should().BeFalse();
        result.CreatedProjectBillingStatusOperationalRegister.Should().BeFalse();
        result.CreatedArOpenItemsOperationalRegister.Should().BeFalse();
        result.CreatedAccountingPolicy.Should().BeFalse();
        state.CreatedAccounts.Should().BeEmpty();
        state.UpdatedAccounts.Should().BeEmpty();
        state.UpdatedCatalogTypes.Count(type => type == AgencyBillingCodes.AccountingPolicy).Should().Be(1);
        state.UpdatedCatalogTypes.Count(type => type == AgencyBillingCodes.PaymentTerms).Should().Be(3);
    }

    [Fact]
    public async Task EnsureDefaultsAsync_RepairsMissingOptionalAndRequiredAccountMetadata()
    {
        var cash = Account("1000", AccountType.Asset, StatementSection.Assets, role: CashFlowRole.None);
        var receivable = Account(
            "1100",
            AccountType.Asset,
            StatementSection.Assets,
            [RequiredDimension(AgencyBillingCodes.Client, 1)],
            CashFlowRole.WorkingCapital,
            "wrong_line");
        var revenue = Account(
            "4000",
            AccountType.Income,
            StatementSection.Income,
            [OptionalDimension(AgencyBillingCodes.Client, 1), RequiredDimension(AgencyBillingCodes.Project, 2)]);
        var state = new SetupState
        {
            Accounts = [Admin(cash), Admin(receivable), Admin(revenue)],
            RegistersExist = true,
            PolicyItems = [CatalogItem("Policy")],
            PaymentTermsItems = [CatalogItem("Due on Receipt"), CatalogItem("Net 15"), CatalogItem("Net 30")]
        };
        var harness = CreateHarness(state);

        await harness.Service.EnsureDefaultsAsync();

        state.UpdatedAccounts.Should().HaveCount(4);
        state.UpdatedAccounts.Should().Contain(request => request.AccountId == cash.Id && request.CashFlowRole == CashFlowRole.CashEquivalent);
        state.UpdatedAccounts.Should().Contain(request => request.AccountId == receivable.Id && request.DimensionRules != null);
        state.UpdatedAccounts.Should().Contain(request => request.AccountId == receivable.Id && request.CashFlowLineCode == CashFlowSystemLineCodes.WorkingCapitalAccountsReceivable);
        state.UpdatedAccounts.Should().Contain(request => request.AccountId == revenue.Id && request.DimensionRules != null);
    }

    [Fact]
    public async Task EnsureDefaultsAsync_MultiplePoliciesFailFast()
    {
        var state = new SetupState
        {
            PolicyItems = [CatalogItem("First"), CatalogItem("Second")]
        };
        var harness = CreateHarness(state);
        Func<Task> act = () => harness.Service.EnsureDefaultsAsync();

        await act.Should().ThrowAsync<NgbConfigurationViolationException>().WithMessage("*Multiple*");
    }

    [Theory]
    [InlineData("cash-deleted")]
    [InlineData("cash-inactive")]
    [InlineData("cash-type")]
    [InlineData("cash-section")]
    [InlineData("cash-dimension")]
    [InlineData("ar-deleted")]
    [InlineData("ar-inactive")]
    [InlineData("ar-type")]
    [InlineData("ar-section")]
    public async Task EnsureDefaultsAsync_RejectsIncompatibleExistingAccounts(string scenario)
    {
        var accounts = ScenarioAccounts(scenario);
        var harness = CreateHarness(new SetupState { Accounts = accounts });
        Func<Task> act = () => harness.Service.EnsureDefaultsAsync();

        await act.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task EnsureDefaultsAsync_WrapsDimensionImmutabilityFailure()
    {
        var accounts = ValidAccounts().ToArray();
        accounts[1] = Admin(Account("1100", AccountType.Asset, StatementSection.Assets));
        var state = new SetupState
        {
            Accounts = accounts,
            UpdateFailure = request => request.DimensionRules is not null
                ? new AccountHasMovementsImmutabilityViolationException(request.AccountId, ["dimensionRules"])
                : null
        };
        var harness = CreateHarness(state);
        Func<Task> act = () => harness.Service.EnsureDefaultsAsync();

        var exception = await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        exception.Which.InnerException.Should().BeOfType<AccountHasMovementsImmutabilityViolationException>();
        exception.Which.Message.Should().Contain("dimension rules cannot be updated");
    }

    [Fact]
    public async Task EnsureDefaultsAsync_WrapsCashFlowImmutabilityFailure()
    {
        var accounts = ValidAccounts().ToArray();
        accounts[0] = Admin(Account("1000", AccountType.Asset, StatementSection.Assets, role: CashFlowRole.None));
        var state = new SetupState
        {
            Accounts = accounts,
            UpdateFailure = request => request.CashFlowRole is not null
                ? new AccountHasMovementsImmutabilityViolationException(request.AccountId, ["cashFlowRole"])
                : null
        };
        var harness = CreateHarness(state);
        Func<Task> act = () => harness.Service.EnsureDefaultsAsync();

        var exception = await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        exception.Which.InnerException.Should().BeOfType<AccountHasMovementsImmutabilityViolationException>();
        exception.Which.Message.Should().Contain("cash flow metadata");
    }

    private static SetupHarness CreateHarness(SetupState state)
    {
        var admin = new Mock<IChartOfAccountsAdminService>(MockBehavior.Strict);
        admin.Setup(x => x.GetAsync(true, It.IsAny<CancellationToken>())).ReturnsAsync(state.Accounts);

        var accountIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var management = new Mock<IChartOfAccountsManagementService>(MockBehavior.Strict);
        management.Setup(x => x.CreateAsync(It.IsAny<CreateAccountRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateAccountRequest, CancellationToken>((request, _) => state.CreatedAccounts.Add(request))
            .ReturnsAsync((CreateAccountRequest request, CancellationToken _) =>
            {
                var id = Guid.CreateVersion7();
                accountIds[request.Code] = id;
                return id;
            });
        management.Setup(x => x.UpdateAsync(It.IsAny<UpdateAccountRequest>(), It.IsAny<CancellationToken>()))
            .Callback<UpdateAccountRequest, CancellationToken>((request, _) => state.UpdatedAccounts.Add(request))
            .Returns((UpdateAccountRequest request, CancellationToken _) =>
            {
                var failure = state.UpdateFailure?.Invoke(request);
                return failure is null ? Task.CompletedTask : Task.FromException(failure);
            });

        var registerIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var registerManagement = new Mock<IOperationalRegisterManagementService>(MockBehavior.Strict);
        registerManagement.Setup(x => x.UpsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((code, _, _) => state.UpsertedRegisterCodes.Add(code))
            .ReturnsAsync((string code, string _, CancellationToken _) =>
            {
                var id = OperationalRegisterId.FromCode(code);
                registerIds[code] = id;
                return id;
            });
        registerManagement.Setup(x => x.ReplaceResourcesAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyList<OperationalRegisterResourceDefinition>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyList<OperationalRegisterResourceDefinition>, CancellationToken>(
                (id, resources, _) => state.ReplacedResources.Add((id, resources)))
            .Returns(Task.CompletedTask);
        registerManagement.Setup(x => x.ReplaceDimensionRulesAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyList<OperationalRegisterDimensionRule>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyList<OperationalRegisterDimensionRule>, CancellationToken>(
                (id, rules, _) => state.ReplacedDimensionRules.Add((id, rules)))
            .Returns(Task.CompletedTask);

        var repository = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string code, CancellationToken _) => state.RegistersExist
                ? Register(OperationalRegisterId.FromCode(code), code)
                : null);

        var maintenance = new Mock<IOperationalRegisterAdminMaintenanceService>(MockBehavior.Strict);
        maintenance.Setup(x => x.EnsurePhysicalSchemaByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => state.EnsuredRegisterIds.Add(id))
            .ReturnsAsync((OperationalRegisterPhysicalSchemaHealth?)null);

        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetPageAsync(It.IsAny<string>(), It.IsAny<PageRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string type, PageRequestDto request, CancellationToken _) =>
            {
                IReadOnlyList<CatalogItemDto> items = type == AgencyBillingCodes.AccountingPolicy
                    ? state.PolicyItems
                    : state.PaymentTermsItems;
                return new PageResponseDto<CatalogItemDto>(items, request.Offset, request.Limit, items.Count);
            });
        catalogs.Setup(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()))
            .Callback<string, RecordPayload, CancellationToken>((type, _, _) => state.CreatedCatalogTypes.Add(type))
            .ReturnsAsync((string _, RecordPayload payload, CancellationToken _) =>
                new CatalogItemDto(Guid.CreateVersion7(), Display(payload), payload, false, false));
        catalogs.Setup(x => x.UpdateAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, RecordPayload, CancellationToken>((type, _, _, _) => state.UpdatedCatalogTypes.Add(type))
            .ReturnsAsync((string _, Guid id, RecordPayload payload, CancellationToken _) =>
                new CatalogItemDto(id, Display(payload), payload, false, false));

        return new SetupHarness(new AgencyBillingSetupService(
            admin.Object,
            management.Object,
            registerManagement.Object,
            repository.Object,
            maintenance.Object,
            catalogs.Object));
    }

    private static IReadOnlyList<ChartOfAccountsAdminItem> ScenarioAccounts(string scenario)
    {
        var valid = ValidAccounts().ToArray();
        if (scenario.StartsWith("cash", StringComparison.Ordinal))
        {
            var account = scenario switch
            {
                "cash-type" => Account("1000", AccountType.Income, StatementSection.Income),
                "cash-section" => Account("1000", AccountType.Asset, StatementSection.Liabilities),
                "cash-dimension" => Account(
                    "1000", AccountType.Asset, StatementSection.Assets,
                    [RequiredDimension("department", 1)], CashFlowRole.CashEquivalent),
                _ => Account("1000", AccountType.Asset, StatementSection.Assets, role: CashFlowRole.CashEquivalent)
            };
            valid[0] = Admin(
                account,
                active: scenario != "cash-inactive",
                deleted: scenario == "cash-deleted");
        }
        else
        {
            var account = scenario switch
            {
                "ar-type" => Account("1100", AccountType.Income, StatementSection.Income),
                "ar-section" => Account("1100", AccountType.Asset, StatementSection.Liabilities),
                _ => Account(
                    "1100", AccountType.Asset, StatementSection.Assets,
                    RequiredDimensions(), CashFlowRole.WorkingCapital,
                    CashFlowSystemLineCodes.WorkingCapitalAccountsReceivable)
            };
            valid[1] = Admin(
                account,
                active: scenario != "ar-inactive",
                deleted: scenario == "ar-deleted");
        }

        return valid;
    }

    private static IReadOnlyList<ChartOfAccountsAdminItem> ValidAccounts() =>
        [
            Admin(Account("1000", AccountType.Asset, StatementSection.Assets, role: CashFlowRole.CashEquivalent)),
            Admin(Account(
                "1100", AccountType.Asset, StatementSection.Assets,
                RequiredDimensions(), CashFlowRole.WorkingCapital,
                CashFlowSystemLineCodes.WorkingCapitalAccountsReceivable)),
            Admin(Account("4000", AccountType.Income, StatementSection.Income, RequiredDimensions()))
        ];

    private static Account Account(
        string code,
        AccountType type,
        StatementSection section,
        IReadOnlyList<AccountDimensionRule>? dimensions = null,
        CashFlowRole role = CashFlowRole.None,
        string? lineCode = null) =>
        new(Guid.CreateVersion7(), code, code, type, section, dimensionRules: dimensions,
            cashFlowRole: role, cashFlowLineCode: lineCode);

    private static IReadOnlyList<AccountDimensionRule> RequiredDimensions() =>
        [
            RequiredDimension(AgencyBillingCodes.Client, 1),
            RequiredDimension(AgencyBillingCodes.Project, 2)
        ];

    private static AccountDimensionRule RequiredDimension(string code, int ordinal) =>
        new(Guid.CreateVersion7(), code, ordinal, true);

    private static AccountDimensionRule OptionalDimension(string code, int ordinal) =>
        new(Guid.CreateVersion7(), code, ordinal, false);

    private static ChartOfAccountsAdminItem Admin(Account account, bool active = true, bool deleted = false) =>
        new() { Account = account, IsActive = active, IsDeleted = deleted };

    private static CatalogItemDto CatalogItem(string display) =>
        new(Guid.CreateVersion7(), display, new RecordPayload(), false, false);

    private static OperationalRegisterAdminItem Register(Guid id, string code) =>
        new(id, code, code.ToLowerInvariant(), code.Replace('.', '_'), code, false,
            DateTime.UnixEpoch, DateTime.UnixEpoch);

    private static string? Display(RecordPayload payload) =>
        payload.Fields is not null && payload.Fields.TryGetValue("display", out var value)
            ? value.GetString()
            : null;

    private sealed record SetupHarness(AgencyBillingSetupService Service);

    private sealed class SetupState
    {
        public IReadOnlyList<ChartOfAccountsAdminItem> Accounts { get; init; } = [];
        public bool RegistersExist { get; init; }
        public IReadOnlyList<CatalogItemDto> PolicyItems { get; init; } = [];
        public IReadOnlyList<CatalogItemDto> PaymentTermsItems { get; init; } = [];
        public Func<UpdateAccountRequest, Exception?>? UpdateFailure { get; init; }
        public List<CreateAccountRequest> CreatedAccounts { get; } = [];
        public List<UpdateAccountRequest> UpdatedAccounts { get; } = [];
        public List<string> UpsertedRegisterCodes { get; } = [];
        public List<(Guid Id, IReadOnlyList<OperationalRegisterResourceDefinition> Resources)> ReplacedResources { get; } = [];
        public List<(Guid Id, IReadOnlyList<OperationalRegisterDimensionRule> Rules)> ReplacedDimensionRules { get; } = [];
        public List<Guid> EnsuredRegisterIds { get; } = [];
        public List<string> CreatedCatalogTypes { get; } = [];
        public List<string> UpdatedCatalogTypes { get; } = [];
    }
}
