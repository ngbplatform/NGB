using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.CashFlow;
using NGB.Accounting.Dimensions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Persistence.OperationalRegisters;
using NGB.Runtime.Accounts;
using NGB.Runtime.Accounts.Exceptions;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Setup;

public sealed class PropertyManagementSetupServiceEdgeCoverageTests
{
    [Fact]
    public async Task Account_guards_reject_blank_code_and_every_incompatible_existing_shape()
    {
        var service = new Harness().Service;
        await AssertThrows<NgbArgumentRequiredException>(() => InvokeAccount(service, [], " "));

        foreach (var item in new[]
                 {
                     Admin(Account("1100", AccountType.Asset, StatementSection.Assets), deleted: true),
                     Admin(Account("1100", AccountType.Asset, StatementSection.Assets), active: false),
                     Admin(Account("1100", AccountType.Income, StatementSection.Income)),
                     Admin(Account("1100", AccountType.Asset, StatementSection.Liabilities))
                 })
        {
            await AssertThrows<NgbConfigurationViolationException>(() => InvokeAccount(service, [item], "1100"));
        }
    }

    [Fact]
    public async Task Cash_account_guards_reject_blank_code_every_shape_and_required_dimensions()
    {
        var service = new Harness().Service;
        await AssertThrows<NgbArgumentRequiredException>(() => InvokeCash(service, [], " "));

        foreach (var item in new[]
                 {
                     Admin(Account("1000", AccountType.Asset, StatementSection.Assets), deleted: true),
                     Admin(Account("1000", AccountType.Asset, StatementSection.Assets), active: false),
                     Admin(Account("1000", AccountType.Income, StatementSection.Income)),
                     Admin(Account("1000", AccountType.Asset, StatementSection.Liabilities)),
                     Admin(Account("1000", AccountType.Asset, StatementSection.Assets,
                         [Dimension("department", required: true)]))
                 })
        {
            await AssertThrows<NgbConfigurationViolationException>(() => InvokeCash(service, [item], "1000"));
        }
    }

    [Fact]
    public void Required_dimension_verifier_covers_empty_missing_optional_and_valid_rules()
    {
        var method = typeof(PropertyManagementSetupService).GetMethod(
            "EnsureHasRequiredDimension",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var required = new[] { PropertyManagementCodes.Party };

        AssertReflectionThrows<NgbConfigurationViolationException>(() =>
            method.Invoke(null, [Account("1100", AccountType.Asset, StatementSection.Assets), PropertyManagementCodes.Party, required]));
        AssertReflectionThrows<NgbConfigurationViolationException>(() =>
            method.Invoke(null, [Account("1100", AccountType.Asset, StatementSection.Assets, [Dimension("other")]), PropertyManagementCodes.Party, required]));
        AssertReflectionThrows<NgbConfigurationViolationException>(() =>
            method.Invoke(null, [Account("1100", AccountType.Asset, StatementSection.Assets, [Dimension(PropertyManagementCodes.Party)]), PropertyManagementCodes.Party, required]));

        method.Invoking(x => x.Invoke(
                null,
                [Account("1100", AccountType.Asset, StatementSection.Assets, [Dimension(PropertyManagementCodes.Party, true)]), PropertyManagementCodes.Party, required]))
            .Should().NotThrow();

        var predicate = typeof(PropertyManagementSetupService).GetMethod(
            "HasRequiredDimension",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        ((bool)predicate.Invoke(null, [Account("1100", AccountType.Asset, StatementSection.Assets), PropertyManagementCodes.Party])!)
            .Should().BeFalse();
        ((bool)predicate.Invoke(null, [Account("1100", AccountType.Asset, StatementSection.Assets, [Dimension("other")]), PropertyManagementCodes.Party])!)
            .Should().BeFalse();
        ((bool)predicate.Invoke(null, [Account("1100", AccountType.Asset, StatementSection.Assets, [Dimension(PropertyManagementCodes.Party)]), PropertyManagementCodes.Party])!)
            .Should().BeFalse();
        ((bool)predicate.Invoke(null, [Account("1100", AccountType.Asset, StatementSection.Assets, [Dimension(PropertyManagementCodes.Party, true)]), PropertyManagementCodes.Party])!)
            .Should().BeTrue();
    }

    [Fact]
    public async Task Dimension_repair_covers_success_immutability_disappearance_and_invalid_refresh()
    {
        var existing = Admin(Account("1100", AccountType.Asset, StatementSection.Assets));
        var validRefresh = Admin(Account(
            "1100", AccountType.Asset, StatementSection.Assets,
            [Dimension(PropertyManagementCodes.Party, true)]));
        var success = new Harness { Accounts = [validRefresh] };
        await InvokeDimensionRepair(success.Service, existing);
        success.Updates.Should().ContainSingle();

        var immutable = new Harness
        {
            Accounts = [validRefresh],
            UpdateFailure = _ => new AccountHasMovementsImmutabilityViolationException(existing.Account.Id, ["dimensionRules"])
        };
        await AssertThrows<NgbConfigurationViolationException>(() => InvokeDimensionRepair(immutable.Service, existing));

        await AssertThrows<NgbConfigurationViolationException>(() =>
            InvokeDimensionRepair(new Harness { Accounts = [] }.Service, existing));
        await AssertThrows<NgbConfigurationViolationException>(() =>
            InvokeDimensionRepair(new Harness { Accounts = [existing] }.Service, existing));

        var optional = Admin(Account(
            "1100", AccountType.Asset, StatementSection.Assets,
            [Dimension(PropertyManagementCodes.Party)]));
        await AssertThrows<NgbConfigurationViolationException>(() =>
            InvokeDimensionRepair(new Harness { Accounts = [optional] }.Service, existing));
    }

    [Fact]
    public async Task Cash_flow_repair_covers_immutability_disappearance_role_line_mismatch_and_success()
    {
        var existing = Admin(Account(
            "1100", AccountType.Asset, StatementSection.Assets,
            role: CashFlowRole.None));
        const string expectedLine = "working-capital";

        var immutable = new Harness
        {
            Accounts = [],
            UpdateFailure = _ => new AccountHasMovementsImmutabilityViolationException(existing.Account.Id, ["cashFlowRole"])
        };
        await AssertThrows<NgbConfigurationViolationException>(() =>
            InvokeCashFlowRepair(immutable.Service, existing, expectedLine));

        var immutableWithoutLine = new Harness
        {
            Accounts = [],
            UpdateFailure = _ => new AccountHasMovementsImmutabilityViolationException(existing.Account.Id, ["cashFlowRole"])
        };
        await AssertThrows<NgbConfigurationViolationException>(() =>
            InvokeCashFlowRepair(immutableWithoutLine.Service, existing, null));

        await AssertThrows<NgbConfigurationViolationException>(() =>
            InvokeCashFlowRepair(new Harness { Accounts = [] }.Service, existing, expectedLine));

        var wrongRole = Admin(Account(
            "1100", AccountType.Asset, StatementSection.Assets,
            role: CashFlowRole.None, lineCode: expectedLine));
        await AssertThrows<NgbConfigurationViolationException>(() =>
            InvokeCashFlowRepair(new Harness { Accounts = [wrongRole] }.Service, existing, expectedLine));

        var wrongLine = Admin(Account(
            "1100", AccountType.Asset, StatementSection.Assets,
            role: CashFlowRole.WorkingCapital, lineCode: "wrong"));
        await AssertThrows<NgbConfigurationViolationException>(() =>
            InvokeCashFlowRepair(new Harness { Accounts = [wrongLine] }.Service, existing, expectedLine));

        var valid = Admin(Account(
            "1100", AccountType.Asset, StatementSection.Assets,
            role: CashFlowRole.WorkingCapital, lineCode: expectedLine));
        await InvokeCashFlowRepair(new Harness { Accounts = [valid] }.Service, existing, $" {expectedLine} ");
    }

    [Fact]
    public async Task Bank_account_gl_validation_covers_every_payload_and_linked_account_state()
    {
        await AssertBankFailure(new RecordPayload(), []);
        await AssertBankFailure(Payload(("other", "value")), []);
        await AssertBankFailure(Payload(("gl_account_id", "invalid")), []);
        await AssertBankFailure(Payload(("gl_account_id", Guid.CreateVersion7())), []);

        var glId = Guid.CreateVersion7();
        await AssertBankFailure(Payload(("gl_account_id", glId)),
            [Admin(Account("1000", AccountType.Asset, StatementSection.Assets, id: glId), deleted: true)]);
        await AssertBankFailure(Payload(("gl_account_id", glId)),
            [Admin(Account("1000", AccountType.Asset, StatementSection.Assets, id: glId), deleted: true)], display: null);
        await AssertBankFailure(Payload(("gl_account_id", glId)),
            [Admin(Account("1000", AccountType.Asset, StatementSection.Assets, id: glId), active: false)]);
        await AssertBankFailure(Payload(("gl_account_id", glId)),
            [Admin(Account("1000", AccountType.Asset, StatementSection.Assets, id: glId), active: false)], display: null);
        await AssertBankFailure(Payload(("gl_account_id", glId)),
            [Admin(Account("1000", AccountType.Income, StatementSection.Income, id: glId))]);
        await AssertBankFailure(Payload(("gl_account_id", glId)),
            [Admin(Account("1000", AccountType.Income, StatementSection.Income, id: glId))], display: null);
        await AssertBankFailure(Payload(("gl_account_id", glId)),
            [Admin(Account("1000", AccountType.Asset, StatementSection.Liabilities, id: glId))]);
        await AssertBankFailure(Payload(("gl_account_id", glId)),
            [Admin(Account("1000", AccountType.Asset, StatementSection.Liabilities, id: glId))], display: null);

        var valid = Admin(Account(
            "1000", AccountType.Asset, StatementSection.Assets,
            role: CashFlowRole.CashEquivalent, id: glId));
        await InvokeBankValidation(new Harness
        {
            Accounts = [valid],
            CatalogPages = request => request.Offset == 0
                ? Page([Catalog(Payload(("gl_account_id", glId)), display: null)])
                : Page([])
        }.Service);
    }

    [Fact]
    public async Task Bank_account_paging_continues_at_full_page_and_duplicate_singletons_fail_fast()
    {
        var glId = Guid.CreateVersion7();
        var valid = Admin(Account(
            "1000", AccountType.Asset, StatementSection.Assets,
            role: CashFlowRole.CashEquivalent, id: glId));
        var fullPage = Enumerable.Range(0, 200)
            .Select(_ => Catalog(Payload(("gl_account_id", glId))))
            .ToArray();
        await InvokeBankValidation(new Harness
        {
            Accounts = [valid],
            CatalogPages = request => request.Offset == 0 ? Page(fullPage) : Page([])
        }.Service);

        var duplicates = Page([Catalog(new RecordPayload()), Catalog(new RecordPayload())]);
        var harness = new Harness { CatalogPages = _ => duplicates };
        await AssertThrows<NgbConfigurationViolationException>(() => InvokeDefaultBank(harness.Service));
        await AssertThrows<NgbConfigurationViolationException>(() => InvokeAccountingPolicy(harness.Service));
    }

    private static async Task AssertBankFailure(
        RecordPayload payload,
        IReadOnlyList<ChartOfAccountsAdminItem> accounts,
        string? display = "Bank")
    {
        var harness = new Harness
        {
            Accounts = accounts,
            CatalogPages = _ => Page([Catalog(payload, display)])
        };
        await AssertThrows<NgbConfigurationViolationException>(() => InvokeBankValidation(harness.Service));
    }

    private sealed class Harness
    {
        private readonly Mock<IChartOfAccountsAdminService> _admin = new(MockBehavior.Strict);
        private readonly Mock<IChartOfAccountsManagementService> _management = new(MockBehavior.Strict);
        private readonly Mock<IOperationalRegisterManagementService> _opreg = new(MockBehavior.Loose);
        private readonly Mock<IOperationalRegisterRepository> _opregRepo = new(MockBehavior.Loose);
        private readonly Mock<IOperationalRegisterAdminMaintenanceService> _maintenance = new(MockBehavior.Loose);
        private readonly Mock<ICatalogService> _catalogs = new(MockBehavior.Strict);

        public IReadOnlyList<ChartOfAccountsAdminItem> Accounts { get; init; } = [];
        public Func<UpdateAccountRequest, Exception?>? UpdateFailure { get; init; }
        public Func<PageRequestDto, PageResponseDto<CatalogItemDto>> CatalogPages { get; init; } = _ => Page([]);
        public List<UpdateAccountRequest> Updates { get; } = [];

        public PropertyManagementSetupService Service
        {
            get
            {
                _admin.Setup(x => x.GetAsync(true, It.IsAny<CancellationToken>())).ReturnsAsync(Accounts);
                _management.Setup(x => x.UpdateAsync(It.IsAny<UpdateAccountRequest>(), It.IsAny<CancellationToken>()))
                    .Callback<UpdateAccountRequest, CancellationToken>((request, _) => Updates.Add(request))
                    .Returns((UpdateAccountRequest request, CancellationToken _) =>
                    {
                        var error = UpdateFailure?.Invoke(request);
                        return error is null ? Task.CompletedTask : Task.FromException(error);
                    });
                _catalogs.Setup(x => x.GetPageAsync(
                        It.IsAny<string>(),
                        It.IsAny<PageRequestDto>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string _, PageRequestDto request, CancellationToken _) => CatalogPages(request));
                return new PropertyManagementSetupService(
                    _admin.Object,
                    _management.Object,
                    _opreg.Object,
                    _opregRepo.Object,
                    _maintenance.Object,
                    _catalogs.Object);
            }
        }
    }

    private static Task InvokeAccount(
        PropertyManagementSetupService service,
        IReadOnlyList<ChartOfAccountsAdminItem> accounts,
        string code)
        => InvokeTask(service, "EnsureAccountAsync",
            accounts, code, "Account", AccountType.Asset, StatementSection.Assets,
            Array.Empty<string>(), CancellationToken.None, CashFlowRole.None, null);

    private static Task InvokeCash(
        PropertyManagementSetupService service,
        IReadOnlyList<ChartOfAccountsAdminItem> accounts,
        string code)
        => InvokeTask(service, "EnsureCashAccountAsync", accounts, code, "Cash", CancellationToken.None);

    private static Task InvokeDimensionRepair(PropertyManagementSetupService service, ChartOfAccountsAdminItem existing)
        => InvokeTask(service, "EnsureOrRepairRequiredDimensionsAsync",
            existing, new[] { PropertyManagementCodes.Party }, CancellationToken.None);

    private static Task InvokeCashFlowRepair(
        PropertyManagementSetupService service,
        ChartOfAccountsAdminItem existing,
        string? lineCode)
        => InvokeTask(service, "EnsureOrRepairCashFlowMetadataAsync",
            existing, CashFlowRole.WorkingCapital, lineCode, CancellationToken.None);

    private static Task InvokeBankValidation(PropertyManagementSetupService service)
        => InvokeTask(service, "EnsureBankAccountGlAccountsAsync", CancellationToken.None);

    private static Task InvokeDefaultBank(PropertyManagementSetupService service)
        => InvokeTask(service, "EnsureDefaultBankAccountAsync", Guid.CreateVersion7(), CancellationToken.None);

    private static Task InvokeAccountingPolicy(PropertyManagementSetupService service)
        => InvokeTask(service, "EnsureAccountingPolicyAsync",
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            CancellationToken.None);

    private static Task InvokeTask(object target, string methodName, params object?[] arguments)
        => (Task)target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(target, arguments)!;

    private static void AssertReflectionThrows<T>(Action action) where T : Exception
        => action.Should().Throw<TargetInvocationException>().Which.InnerException.Should().BeOfType<T>();

    private static Task AssertThrows<T>(Func<Task> action) where T : Exception
        => action.Should().ThrowAsync<T>();

    private static Account Account(
        string code,
        AccountType type,
        StatementSection section,
        IReadOnlyList<AccountDimensionRule>? dimensions = null,
        CashFlowRole role = CashFlowRole.None,
        string? lineCode = null,
        Guid? id = null)
        => new(id ?? Guid.CreateVersion7(), code, code, type, section,
            dimensionRules: dimensions, cashFlowRole: role, cashFlowLineCode: lineCode);

    private static AccountDimensionRule Dimension(string code, bool required = false)
        => new(Guid.CreateVersion7(), code, 1, required);

    private static ChartOfAccountsAdminItem Admin(Account account, bool active = true, bool deleted = false)
        => new() { Account = account, IsActive = active, IsDeleted = deleted };

    private static RecordPayload Payload(params (string Key, object? Value)[] fields)
        => new(fields.ToDictionary(
            pair => pair.Key,
            pair => JsonSerializer.SerializeToElement(pair.Value),
            StringComparer.Ordinal));

    private static CatalogItemDto Catalog(RecordPayload payload, string? display = "Bank")
        => new(Guid.CreateVersion7(), display, payload, false, false);

    private static PageResponseDto<CatalogItemDto> Page(IReadOnlyList<CatalogItemDto> items)
        => new(items, 0, Math.Max(items.Count, 1), items.Count);
}
