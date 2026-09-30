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
using NGB.Persistence.ReferenceRegisters;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Accounts;
using NGB.Runtime.Accounts.Exceptions;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Trade.Runtime.Tests.Setup;

public sealed class TradeSetupServiceFullCoverageTests
{
    [Fact]
    public async Task EnsureDefaultsAsync_FirstRunCreatesAllAccountsRegistersPolicyAndCatalogDefaults()
    {
        var state = new SetupState();

        var result = await CreateService(state).EnsureDefaultsAsync();

        result.CreatedCashAccount.Should().BeTrue();
        result.CreatedAccountsReceivableAccount.Should().BeTrue();
        result.CreatedInventoryAccount.Should().BeTrue();
        result.CreatedAccountsPayableAccount.Should().BeTrue();
        result.CreatedSalesRevenueAccount.Should().BeTrue();
        result.CreatedCostOfGoodsSoldAccount.Should().BeTrue();
        result.CreatedInventoryAdjustmentAccount.Should().BeTrue();
        result.CreatedInventoryMovementsOperationalRegister.Should().BeTrue();
        result.CreatedItemPricesReferenceRegister.Should().BeTrue();
        result.CreatedAccountingPolicy.Should().BeTrue();
        state.CreatedAccounts.Select(x => x.Code).Should().Equal("1000", "1100", "1200", "2000", "4000", "5000", "5200");
        state.OperationalResources.Should().ContainSingle().Which.Resources.Should().HaveCount(3);
        state.OperationalDimensions.Should().ContainSingle().Which.Rules.Should().HaveCount(2);
        state.ReferenceFields.Should().ContainSingle().Which.Fields.Should().HaveCount(5);
        state.ReferenceDimensions.Should().ContainSingle().Which.Rules.Should().HaveCount(2);
        state.EnsuredOperationalSchemas.Should().ContainSingle();
        state.EnsuredReferenceSchemas.Should().ContainSingle();
        state.CatalogCreates.Should().HaveCount(13);
    }

    [Fact]
    public async Task EnsureDefaultsAsync_ExistingCompatibleStateIsIdempotentAndUpdatesAllCatalogDefaults()
    {
        var state = new SetupState
        {
            Accounts = ValidAccounts(),
            OperationalRegisterExists = true,
            ReferenceRegisterExists = true,
            PolicyItems = [Catalog("Existing policy")]
        };
        state.AddDefaultCatalogItems();

        var result = await CreateService(state).EnsureDefaultsAsync();

        result.CreatedCashAccount.Should().BeFalse();
        result.CreatedAccountsReceivableAccount.Should().BeFalse();
        result.CreatedInventoryAccount.Should().BeFalse();
        result.CreatedAccountsPayableAccount.Should().BeFalse();
        result.CreatedSalesRevenueAccount.Should().BeFalse();
        result.CreatedCostOfGoodsSoldAccount.Should().BeFalse();
        result.CreatedInventoryAdjustmentAccount.Should().BeFalse();
        result.CreatedInventoryMovementsOperationalRegister.Should().BeFalse();
        result.CreatedItemPricesReferenceRegister.Should().BeFalse();
        result.CreatedAccountingPolicy.Should().BeFalse();
        state.CreatedAccounts.Should().BeEmpty();
        state.UpdatedAccounts.Should().BeEmpty();
        state.CatalogUpdates.Should().HaveCount(13);
        state.CatalogCreates.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureDefaultsAsync_RepairsMissingRequiredDimensionsAndCashFlowMetadata()
    {
        var accounts = ValidAccounts().ToArray();
        accounts[0] = Admin(Account("1000", AccountType.Asset, StatementSection.Assets, role: CashFlowRole.None));
        accounts[1] = Admin(Account(
            "1100", AccountType.Asset, StatementSection.Assets,
            [OptionalDimension(TradeCodes.Party, 1)], CashFlowRole.WorkingCapital, " wrong-line "));
        var state = new SetupState { Accounts = accounts };

        await CreateService(state).EnsureDefaultsAsync();

        state.UpdatedAccounts.Should().HaveCount(3);
        state.UpdatedAccounts.Should().Contain(x => x.AccountId == accounts[0].Account.Id && x.CashFlowRole == CashFlowRole.CashEquivalent);
        state.UpdatedAccounts.Should().Contain(x => x.AccountId == accounts[1].Account.Id && x.DimensionRules != null);
        state.UpdatedAccounts.Should().Contain(x => x.AccountId == accounts[1].Account.Id && x.CashFlowLineCode == CashFlowSystemLineCodes.WorkingCapitalAccountsReceivable);
    }

    [Fact]
    public async Task EnsureDefaultsAsync_MultiplePoliciesFailFast()
    {
        var state = new SetupState { PolicyItems = [Catalog("One"), Catalog("Two")] };
        var act = () => CreateService(state).EnsureDefaultsAsync();

        await act.Should().ThrowAsync<NgbConfigurationViolationException>().WithMessage("*Multiple*");
    }

    [Theory]
    [InlineData("cash-deleted")]
    [InlineData("cash-inactive")]
    [InlineData("cash-type")]
    [InlineData("cash-section")]
    [InlineData("cash-dimension")]
    [InlineData("account-deleted")]
    [InlineData("account-inactive")]
    [InlineData("account-type")]
    [InlineData("account-section")]
    public async Task EnsureDefaultsAsync_RejectsEveryIncompatibleExistingAccountShape(string scenario)
    {
        var act = () => CreateService(new SetupState { Accounts = ScenarioAccounts(scenario) }).EnsureDefaultsAsync();

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
        var act = () => CreateService(state).EnsureDefaultsAsync();

        var error = await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        error.Which.InnerException.Should().BeOfType<AccountHasMovementsImmutabilityViolationException>();
        error.Which.Message.Should().Contain("dimension rules cannot be updated");
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
        var act = () => CreateService(state).EnsureDefaultsAsync();

        var error = await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        error.Which.InnerException.Should().BeOfType<AccountHasMovementsImmutabilityViolationException>();
        error.Which.Message.Should().Contain("cash flow metadata");
    }

    private static TradeSetupService CreateService(SetupState state)
    {
        var admin = new Mock<IChartOfAccountsAdminService>(MockBehavior.Strict);
        admin.Setup(x => x.GetAsync(true, It.IsAny<CancellationToken>())).ReturnsAsync(state.Accounts);

        var accounts = new Mock<IChartOfAccountsManagementService>(MockBehavior.Strict);
        accounts.Setup(x => x.CreateAsync(It.IsAny<CreateAccountRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateAccountRequest, CancellationToken>((request, _) => state.CreatedAccounts.Add(request))
            .ReturnsAsync((CreateAccountRequest _, CancellationToken _) => Guid.CreateVersion7());
        accounts.Setup(x => x.UpdateAsync(It.IsAny<UpdateAccountRequest>(), It.IsAny<CancellationToken>()))
            .Callback<UpdateAccountRequest, CancellationToken>((request, _) => state.UpdatedAccounts.Add(request))
            .Returns((UpdateAccountRequest request, CancellationToken _) =>
            {
                var failure = state.UpdateFailure?.Invoke(request);
                return failure is null ? Task.CompletedTask : Task.FromException(failure);
            });

        var operationalId = OperationalRegisterId.FromCode(TradeCodes.InventoryMovementsRegisterCode);
        var operationalManagement = new Mock<IOperationalRegisterManagementService>(MockBehavior.Strict);
        operationalManagement.Setup(x => x.UpsertAsync(
                TradeCodes.InventoryMovementsRegisterCode, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(operationalId);
        operationalManagement.Setup(x => x.ReplaceResourcesAsync(
                operationalId, It.IsAny<IReadOnlyList<OperationalRegisterResourceDefinition>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyList<OperationalRegisterResourceDefinition>, CancellationToken>(
                (id, resources, _) => state.OperationalResources.Add((id, resources)))
            .Returns(Task.CompletedTask);
        operationalManagement.Setup(x => x.ReplaceDimensionRulesAsync(
                operationalId, It.IsAny<IReadOnlyList<OperationalRegisterDimensionRule>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyList<OperationalRegisterDimensionRule>, CancellationToken>(
                (id, rules, _) => state.OperationalDimensions.Add((id, rules)))
            .Returns(Task.CompletedTask);
        var operationalRepository = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        operationalRepository.Setup(x => x.GetByCodeAsync(
                TradeCodes.InventoryMovementsRegisterCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state.OperationalRegisterExists
                ? OperationalRegister(operationalId, TradeCodes.InventoryMovementsRegisterCode)
                : null);
        var operationalMaintenance = new Mock<IOperationalRegisterAdminBatchMaintenanceService>(MockBehavior.Strict);
        operationalMaintenance.Setup(x => x.EnsurePhysicalSchemasByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { operationalId })),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, CancellationToken>((ids, _) => state.EnsuredOperationalSchemas.AddRange(ids))
            .Returns(Task.CompletedTask);

        var referenceId = DeterministicGuid.Create($"ReferenceRegister|{TradeCodes.ItemPricesRegisterCode}");
        var referenceManagement = new Mock<IReferenceRegisterManagementService>(MockBehavior.Strict);
        referenceManagement.Setup(x => x.UpsertAsync(
                TradeCodes.ItemPricesRegisterCode, It.IsAny<string>(), ReferenceRegisterPeriodicity.NonPeriodic,
                ReferenceRegisterRecordMode.Independent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(referenceId);
        referenceManagement.Setup(x => x.ReplaceFieldsAsync(
                referenceId, It.IsAny<IReadOnlyList<ReferenceRegisterFieldDefinition>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyList<ReferenceRegisterFieldDefinition>, CancellationToken>(
                (id, fields, _) => state.ReferenceFields.Add((id, fields)))
            .Returns(Task.CompletedTask);
        referenceManagement.Setup(x => x.ReplaceDimensionRulesAsync(
                referenceId, It.IsAny<IReadOnlyList<ReferenceRegisterDimensionRule>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyList<ReferenceRegisterDimensionRule>, CancellationToken>(
                (id, rules, _) => state.ReferenceDimensions.Add((id, rules)))
            .Returns(Task.CompletedTask);
        var referenceRepository = new Mock<IReferenceRegisterRepository>(MockBehavior.Strict);
        referenceRepository.Setup(x => x.GetByCodeAsync(TradeCodes.ItemPricesRegisterCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state.ReferenceRegisterExists
                ? ReferenceRegister(referenceId, TradeCodes.ItemPricesRegisterCode)
                : null);
        var referenceMaintenance = new Mock<IReferenceRegisterAdminBatchMaintenanceService>(MockBehavior.Strict);
        referenceMaintenance.Setup(x => x.EnsurePhysicalSchemasByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { referenceId })),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, CancellationToken>((ids, _) => state.EnsuredReferenceSchemas.AddRange(ids))
            .Returns(Task.CompletedTask);

        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetPageAsync(It.IsAny<string>(), It.IsAny<PageRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string type, PageRequestDto request, CancellationToken _) =>
            {
                var items = type == TradeCodes.AccountingPolicy
                    ? state.PolicyItems
                    : state.CatalogItems.GetValueOrDefault(type) ?? [];
                return new PageResponseDto<CatalogItemDto>(items, request.Offset, request.Limit, items.Count);
            });
        catalogs.Setup(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()))
            .Callback<string, RecordPayload, CancellationToken>((type, payload, _) => state.CatalogCreates.Add((type, payload)))
            .ReturnsAsync((string _, RecordPayload payload, CancellationToken _) =>
                new CatalogItemDto(Guid.CreateVersion7(), Display(payload), payload, false, false));
        catalogs.Setup(x => x.UpdateAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, RecordPayload, CancellationToken>(
                (type, id, payload, _) => state.CatalogUpdates.Add((type, id, payload)))
            .ReturnsAsync((string _, Guid id, RecordPayload payload, CancellationToken _) =>
                new CatalogItemDto(id, Display(payload), payload, false, false));

        return new TradeSetupService(
            admin.Object, accounts.Object,
            operationalManagement.Object, operationalRepository.Object, operationalMaintenance.Object,
            referenceManagement.Object, referenceRepository.Object, referenceMaintenance.Object,
            catalogs.Object);
    }

    private static IReadOnlyList<ChartOfAccountsAdminItem> ScenarioAccounts(string scenario)
    {
        var accounts = ValidAccounts().ToArray();
        if (scenario.StartsWith("cash", StringComparison.Ordinal))
        {
            var account = scenario switch
            {
                "cash-type" => Account("1000", AccountType.Income, StatementSection.Income),
                "cash-section" => Account("1000", AccountType.Asset, StatementSection.Liabilities),
                "cash-dimension" => Account("1000", AccountType.Asset, StatementSection.Assets,
                    [RequiredDimension("department", 1)], CashFlowRole.CashEquivalent),
                _ => Account("1000", AccountType.Asset, StatementSection.Assets, role: CashFlowRole.CashEquivalent)
            };
            accounts[0] = Admin(account, scenario != "cash-inactive", scenario == "cash-deleted");
        }
        else
        {
            var account = scenario switch
            {
                "account-type" => Account("1100", AccountType.Income, StatementSection.Income),
                "account-section" => Account("1100", AccountType.Asset, StatementSection.Liabilities),
                _ => Account("1100", AccountType.Asset, StatementSection.Assets,
                    PartyDimensions(), CashFlowRole.WorkingCapital,
                    CashFlowSystemLineCodes.WorkingCapitalAccountsReceivable)
            };
            accounts[1] = Admin(account, scenario != "account-inactive", scenario == "account-deleted");
        }

        return accounts;
    }

    private static IReadOnlyList<ChartOfAccountsAdminItem> ValidAccounts() =>
    [
        Admin(Account("1000", AccountType.Asset, StatementSection.Assets, role: CashFlowRole.CashEquivalent)),
        Admin(Account("1100", AccountType.Asset, StatementSection.Assets, PartyDimensions(),
            CashFlowRole.WorkingCapital, CashFlowSystemLineCodes.WorkingCapitalAccountsReceivable)),
        Admin(Account("1200", AccountType.Asset, StatementSection.Assets, InventoryDimensions(),
            CashFlowRole.WorkingCapital, CashFlowSystemLineCodes.WorkingCapitalInventory)),
        Admin(Account("2000", AccountType.Liability, StatementSection.Liabilities, PartyDimensions(),
            CashFlowRole.WorkingCapital, CashFlowSystemLineCodes.WorkingCapitalAccountsPayable)),
        Admin(Account("4000", AccountType.Income, StatementSection.Income, SalesDimensions())),
        Admin(Account("5000", AccountType.Expense, StatementSection.Expenses, InventoryDimensions())),
        Admin(Account("5200", AccountType.Expense, StatementSection.Expenses, InventoryDimensions()))
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

    private static IReadOnlyList<AccountDimensionRule> PartyDimensions() =>
        [RequiredDimension(TradeCodes.Party, 1)];

    private static IReadOnlyList<AccountDimensionRule> InventoryDimensions() =>
        [RequiredDimension(TradeCodes.Item, 1), RequiredDimension(TradeCodes.Warehouse, 2)];

    private static IReadOnlyList<AccountDimensionRule> SalesDimensions() =>
        [RequiredDimension(TradeCodes.Party, 1), RequiredDimension(TradeCodes.Item, 2), RequiredDimension(TradeCodes.Warehouse, 3)];

    private static AccountDimensionRule RequiredDimension(string code, int ordinal) =>
        new(Guid.CreateVersion7(), code, ordinal, true);

    private static AccountDimensionRule OptionalDimension(string code, int ordinal) =>
        new(Guid.CreateVersion7(), code, ordinal, false);

    private static ChartOfAccountsAdminItem Admin(Account account, bool active = true, bool deleted = false) =>
        new() { Account = account, IsActive = active, IsDeleted = deleted };

    private static OperationalRegisterAdminItem OperationalRegister(Guid id, string code) =>
        new(id, code, code, code.Replace('.', '_'), code, false, DateTime.UnixEpoch, DateTime.UnixEpoch);

    private static ReferenceRegisterAdminItem ReferenceRegister(Guid id, string code) =>
        new(id, code, code, code.Replace('.', '_'), code, ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent, false, DateTime.UnixEpoch, DateTime.UnixEpoch);

    private static CatalogItemDto Catalog(string display) =>
        new(Guid.CreateVersion7(), display, new RecordPayload(), false, false);

    private static string? Display(RecordPayload payload) =>
        payload.Fields is not null && payload.Fields.TryGetValue("display", out var value) ? value.GetString() : null;

    private sealed class SetupState
    {
        public IReadOnlyList<ChartOfAccountsAdminItem> Accounts { get; init; } = [];
        public bool OperationalRegisterExists { get; init; }
        public bool ReferenceRegisterExists { get; init; }
        public IReadOnlyList<CatalogItemDto> PolicyItems { get; init; } = [];
        public Dictionary<string, IReadOnlyList<CatalogItemDto>> CatalogItems { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Func<UpdateAccountRequest, Exception?>? UpdateFailure { get; init; }
        public List<CreateAccountRequest> CreatedAccounts { get; } = [];
        public List<UpdateAccountRequest> UpdatedAccounts { get; } = [];
        public List<(Guid Id, IReadOnlyList<OperationalRegisterResourceDefinition> Resources)> OperationalResources { get; } = [];
        public List<(Guid Id, IReadOnlyList<OperationalRegisterDimensionRule> Rules)> OperationalDimensions { get; } = [];
        public List<(Guid Id, IReadOnlyList<ReferenceRegisterFieldDefinition> Fields)> ReferenceFields { get; } = [];
        public List<(Guid Id, IReadOnlyList<ReferenceRegisterDimensionRule> Rules)> ReferenceDimensions { get; } = [];
        public List<Guid> EnsuredOperationalSchemas { get; } = [];
        public List<Guid> EnsuredReferenceSchemas { get; } = [];
        public List<(string Type, RecordPayload Payload)> CatalogCreates { get; } = [];
        public List<(string Type, Guid Id, RecordPayload Payload)> CatalogUpdates { get; } = [];

        public void AddDefaultCatalogItems()
        {
            CatalogItems[TradeCodes.UnitOfMeasure] = [Catalog("each"), Catalog("BOX"), Catalog("Pallet")];
            CatalogItems[TradeCodes.PaymentTerms] = [Catalog("due on receipt"), Catalog("NET 15"), Catalog("Net 30")];
            CatalogItems[TradeCodes.InventoryAdjustmentReason] = [Catalog("count correction"), Catalog("DAMAGE"), Catalog("Shrinkage")];
            CatalogItems[TradeCodes.PriceType] = [Catalog("retail"), Catalog("WHOLESALE"), Catalog("Distributor")];
        }
    }
}
