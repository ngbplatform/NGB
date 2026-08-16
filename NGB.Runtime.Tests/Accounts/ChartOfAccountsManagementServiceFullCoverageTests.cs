using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.CashFlow;
using NGB.Accounting.Dimensions;
using NGB.Core.AuditLog;
using NGB.Persistence.Accounts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Accounts;
using NGB.Runtime.Accounts.Exceptions;
using NGB.Runtime.AuditLog;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.Tests.Accounts;

public sealed class ChartOfAccountsManagementServiceFullCoverageTests
{
    [Fact]
    public async Task Create_RejectsNullAndEveryInvalidDimensionRuleShape()
    {
        var fixture = new Fixture();

        await ((Func<Task>)(() => fixture.Sut.CreateAsync(null!)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        var cases = new[]
        {
            (Rules: (IReadOnlyList<AccountDimensionRuleRequest>)[new(" ")], Reason: "empty_dimension_code"),
            (Rules: (IReadOnlyList<AccountDimensionRuleRequest>)[new("Customer", Ordinal: 0)], Reason: "non_positive_ordinal"),
            (Rules: (IReadOnlyList<AccountDimensionRuleRequest>)[new("Customer", Ordinal: 1), new("Project", Ordinal: 1)], Reason: "duplicate_ordinal"),
            (Rules: (IReadOnlyList<AccountDimensionRuleRequest>)[new("Customer", Ordinal: 1), new(" customer ", Ordinal: 2)], Reason: "duplicate_dimension")
        };

        foreach (var test in cases)
        {
            var act = () => fixture.Sut.CreateAsync(Request(dimensionRules: test.Rules));
            var exception = await act.Should().ThrowAsync<AccountDimensionRulesValidationException>();
            exception.Which.Reason.Should().Be(test.Reason);
        }
    }

    [Fact]
    public async Task Create_PersistsRichRequestAndWritesCompleteAuditInsideTransaction()
    {
        var fixture = new Fixture();
        Account? created = null;
        fixture.Repository.Setup(x => x.CreateAsync(
                It.IsAny<Account>(), false, It.IsAny<CancellationToken>()))
            .Callback<Account, bool, CancellationToken>((account, _, _) => created = account)
            .Returns(Task.CompletedTask);

        var id = await fixture.Sut.CreateAsync(Request(
            isActive: false,
            dimensionRules:
            [
                new(" Project ", true, 20),
                new("Customer", false)
            ]));

        created.Should().NotBeNull();
        created!.Id.Should().Be(id);
        created.DimensionRules.Select(x => (x.DimensionCode, x.Ordinal, x.IsRequired)).Should().Equal(
            ("Project", 20, true),
            ("Customer", 101, false));
        fixture.Audit.Verify(x => x.WriteAsync(
            AuditEntityKind.ChartOfAccountsAccount,
            id,
            AuditActionCodes.CoaAccountCreate,
            It.Is<IReadOnlyList<AuditFieldChange>>(changes => changes.Count == 10),
            It.IsAny<object>(),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Uow.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.Uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_CoversCashFlowValidationNegativeAndBoundaryCases()
    {
        await AssertInvalid(Request(role: CashFlowRole.None, lineCode: "line"));
        await AssertInvalid(Request(role: CashFlowRole.WorkingCapital));
        await AssertInvalid(Request(role: CashFlowRole.WorkingCapital, lineCode: "missing"));
        await AssertInvalid(
            Request(role: CashFlowRole.WorkingCapital, lineCode: "wrong-method"),
            Line("wrong-method", CashFlowSection.Operating, (CashFlowMethod)99));

        await AssertInvalid(Request(
            type: AccountType.Liability,
            section: StatementSection.Liabilities,
            role: CashFlowRole.CashEquivalent));
        await AssertInvalid(Request(
            type: AccountType.Asset,
            section: StatementSection.Liabilities,
            role: CashFlowRole.CashEquivalent));
        await AssertInvalid(Request(
            type: AccountType.Asset,
            section: StatementSection.Assets,
            isContra: true,
            role: CashFlowRole.CashEquivalent));

        await AssertInvalid(Request(
            section: StatementSection.Income,
            role: CashFlowRole.WorkingCapital,
            lineCode: "operating"), Line("operating", CashFlowSection.Operating));
        await AssertInvalid(Request(
            section: StatementSection.Assets,
            role: CashFlowRole.WorkingCapital,
            lineCode: "wrong-section"), Line("wrong-section", CashFlowSection.Investing));
        await AssertInvalid(Request(
            section: StatementSection.Assets,
            role: CashFlowRole.NonCashOperatingAdjustment,
            lineCode: "operating"), Line("operating", CashFlowSection.Operating));
        await AssertInvalid(Request(
            section: StatementSection.Liabilities,
            role: CashFlowRole.InvestingCounterparty,
            lineCode: "investing"), Line("investing", CashFlowSection.Investing));
        await AssertInvalid(Request(
            section: StatementSection.Assets,
            role: CashFlowRole.FinancingCounterparty,
            lineCode: "financing"), Line("financing", CashFlowSection.Financing));
        await AssertInvalid(Request(role: (CashFlowRole)99));
    }

    [Fact]
    public async Task Create_CoversEveryValidCashFlowRoleAndProfitLossSection()
    {
        await AssertValid(Request(role: CashFlowRole.None));
        await AssertValid(Request(
            type: AccountType.Asset,
            section: StatementSection.Assets,
            role: CashFlowRole.CashEquivalent));
        await AssertValid(Request(
            section: StatementSection.Assets,
            role: CashFlowRole.WorkingCapital,
            lineCode: "operating"), Line("operating", CashFlowSection.Operating));
        await AssertValid(Request(
            section: StatementSection.Liabilities,
            role: CashFlowRole.WorkingCapital,
            lineCode: "operating"), Line("operating", CashFlowSection.Operating));

        foreach (var section in new[]
                 {
                     StatementSection.Income,
                     StatementSection.CostOfGoodsSold,
                     StatementSection.Expenses,
                     StatementSection.OtherIncome,
                     StatementSection.OtherExpense
                 })
        {
            await AssertValid(Request(
                section: section,
                role: CashFlowRole.NonCashOperatingAdjustment,
                lineCode: "operating"), Line("operating", CashFlowSection.Operating));
        }

        await AssertValid(Request(
            section: StatementSection.Assets,
            role: CashFlowRole.InvestingCounterparty,
            lineCode: "investing"), Line("investing", CashFlowSection.Investing));
        await AssertValid(Request(
            section: StatementSection.Liabilities,
            role: CashFlowRole.FinancingCounterparty,
            lineCode: "financing"), Line("financing", CashFlowSection.Financing));
        await AssertValid(Request(
            section: StatementSection.Equity,
            role: CashFlowRole.FinancingCounterparty,
            lineCode: "financing"), Line("financing", CashFlowSection.Financing));
    }

    [Fact]
    public async Task Update_CoversNullMissingDeletedNoOpImmutabilityAndAllChangedFields()
    {
        var fixture = new Fixture();
        await ((Func<Task>)(() => fixture.Sut.UpdateAsync(null!)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        fixture.Repository.SetupSequence(x => x.GetAdminByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChartOfAccountsAdminItem?)null)
            .ReturnsAsync(Item(Account(), deleted: true))
            .ReturnsAsync(Item(Account()))
            .ReturnsAsync(Item(Account()))
            .ReturnsAsync(Item(Account()));

        await ((Func<Task>)(() => fixture.Sut.UpdateAsync(new(Guid.NewGuid()))))
            .Should().ThrowAsync<AccountNotFoundException>();
        await ((Func<Task>)(() => fixture.Sut.UpdateAsync(new(Guid.NewGuid()))))
            .Should().ThrowAsync<AccountDeletedException>();
        await fixture.Sut.UpdateAsync(new(Guid.NewGuid()));

        fixture.Repository.SetupSequence(x => x.HasMovementsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);
        var immutablePatch = new UpdateAccountRequest(Guid.NewGuid(), Code: "2000");
        await ((Func<Task>)(() => fixture.Sut.UpdateAsync(immutablePatch)))
            .Should().ThrowAsync<AccountHasMovementsImmutabilityViolationException>();

        var allPatch = new UpdateAccountRequest(
            Guid.NewGuid(),
            Code: "2000",
            Name: "Updated",
            Type: AccountType.Liability,
            StatementSection: StatementSection.Liabilities,
            IsContra: true,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow,
            IsActive: false,
            DimensionRules: [new("Project", true, 1)],
            CashFlowRole: CashFlowRole.FinancingCounterparty,
            CashFlowLineCode: "financing");
        fixture.CashFlowLines.Setup(x => x.GetByCodeAsync("financing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Line("financing", CashFlowSection.Financing));
        await fixture.Sut.UpdateAsync(allPatch);

        fixture.Repository.Verify(x => x.UpdateAsync(
            It.Is<Account>(account => account.Code == "2000"
                                      && account.Name == "Updated"
                                      && account.Type == AccountType.Liability
                                      && account.StatementSection == StatementSection.Liabilities
                                      && account.IsContra
                                      && account.NegativeBalancePolicy == NegativeBalancePolicy.Allow
                                      && account.CashFlowRole == CashFlowRole.FinancingCounterparty
                                      && account.CashFlowLineCode == "financing"
                                      && account.DimensionRules.Count == 1),
            false,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_CoversMutableOnlyPatchAndEveryDimensionEqualityDifference()
    {
        var id = Guid.NewGuid();
        var baseRule = Rule("Customer", required: false, ordinal: 10);
        var current = Account(id: id, rules: [baseRule]);
        var patches = new UpdateAccountRequest[]
        {
            new(id, Name: "Renamed"),
            new(id, DimensionRules: [new("Customer", false, 10), new("Project", false, 20)]),
            new(id, DimensionRules: [new("Project", false, 10)]),
            new(id, DimensionRules: [new("Customer", true, 10)]),
            new(id, DimensionRules: [new("Customer", false, 11)]),
            new(id, DimensionRules: [new("customer", false, 10)])
        };

        foreach (var patch in patches)
        {
            var fixture = new Fixture();
            fixture.Repository.Setup(x => x.GetAdminByIdAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Item(current));
            fixture.Repository.Setup(x => x.HasMovementsAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            await fixture.Sut.UpdateAsync(patch);
        }
    }

    [Fact]
    public async Task Lifecycle_CoversMissingDeletedNoOpMovementGuardAndBothDeleteChangeShapes()
    {
        var id = Guid.NewGuid();
        var fixture = new Fixture();
        fixture.Repository.SetupSequence(x => x.GetAdminByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChartOfAccountsAdminItem?)null)
            .ReturnsAsync(Item(Account(id), deleted: true))
            .ReturnsAsync(Item(Account(id), active: true))
            .ReturnsAsync(Item(Account(id), active: true));

        await ((Func<Task>)(() => fixture.Sut.SetActiveAsync(id, false)))
            .Should().ThrowAsync<AccountNotFoundException>();
        await ((Func<Task>)(() => fixture.Sut.SetActiveAsync(id, false)))
            .Should().ThrowAsync<AccountDeletedException>();
        await fixture.Sut.SetActiveAsync(id, true);
        await fixture.Sut.SetActiveAsync(id, false);
        fixture.Repository.Verify(x => x.SetActiveAsync(id, false, It.IsAny<CancellationToken>()), Times.Once);

        fixture.Repository.SetupSequence(x => x.GetAdminByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChartOfAccountsAdminItem?)null)
            .ReturnsAsync(Item(Account(id), deleted: true))
            .ReturnsAsync(Item(Account(id), active: true))
            .ReturnsAsync(Item(Account(id), active: true))
            .ReturnsAsync(Item(Account(id), active: false));
        fixture.Repository.SetupSequence(x => x.HasMovementsAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false)
            .ReturnsAsync(false);

        await ((Func<Task>)(() => fixture.Sut.MarkForDeletionAsync(id)))
            .Should().ThrowAsync<AccountNotFoundException>();
        await fixture.Sut.MarkForDeletionAsync(id);
        await ((Func<Task>)(() => fixture.Sut.MarkForDeletionAsync(id)))
            .Should().ThrowAsync<AccountHasMovementsCannotDeleteException>();
        await fixture.Sut.MarkForDeletionAsync(id);
        await fixture.Sut.MarkForDeletionAsync(id);
        fixture.Repository.Verify(x => x.MarkForDeletionAsync(id, It.IsAny<CancellationToken>()), Times.Exactly(2));

        fixture.Repository.SetupSequence(x => x.GetAdminByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChartOfAccountsAdminItem?)null)
            .ReturnsAsync(Item(Account(id), deleted: false))
            .ReturnsAsync(Item(Account(id), deleted: true));
        await ((Func<Task>)(() => fixture.Sut.UnmarkForDeletionAsync(id)))
            .Should().ThrowAsync<AccountNotFoundException>();
        await fixture.Sut.UnmarkForDeletionAsync(id);
        await fixture.Sut.UnmarkForDeletionAsync(id);
        fixture.Repository.Verify(x => x.UnmarkForDeletionAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task AssertInvalid(CreateAccountRequest request, CashFlowLineDefinition? line = null)
    {
        var fixture = new Fixture();
        if (line is not null)
        {
            fixture.CashFlowLines.Setup(x => x.GetByCodeAsync(line.LineCode, It.IsAny<CancellationToken>()))
                .ReturnsAsync(line);
        }

        await ((Func<Task>)(() => fixture.Sut.CreateAsync(request)))
            .Should().ThrowAsync<NgbException>();
    }

    private static async Task AssertValid(CreateAccountRequest request, CashFlowLineDefinition? line = null)
    {
        var fixture = new Fixture();
        if (line is not null)
        {
            fixture.CashFlowLines.Setup(x => x.GetByCodeAsync(line.LineCode, It.IsAny<CancellationToken>()))
                .ReturnsAsync(line);
        }

        await fixture.Sut.CreateAsync(request);
    }

    private static CreateAccountRequest Request(
        AccountType type = AccountType.Asset,
        StatementSection? section = StatementSection.Assets,
        bool isContra = false,
        bool isActive = true,
        IReadOnlyList<AccountDimensionRuleRequest>? dimensionRules = null,
        CashFlowRole? role = null,
        string? lineCode = null)
        => new(
            "1000",
            "Account",
            type,
            section,
            isContra,
            NegativeBalancePolicy.Warn,
            isActive,
            dimensionRules,
            role,
            lineCode);

    private static Account Account(
        Guid? id = null,
        IReadOnlyList<AccountDimensionRule>? rules = null)
        => new(
            id ?? Guid.NewGuid(),
            "1000",
            "Account",
            AccountType.Asset,
            StatementSection.Assets,
            NegativeBalancePolicy.Warn,
            dimensionRules: rules);

    private static AccountDimensionRule Rule(string code, bool required, int ordinal)
        => new(DeterministicGuid.Create($"Dimension|{code.Trim().ToLowerInvariant()}"), code, ordinal, required);

    private static ChartOfAccountsAdminItem Item(
        Account account,
        bool active = true,
        bool deleted = false)
        => new() { Account = account, IsActive = active, IsDeleted = deleted };

    private static CashFlowLineDefinition Line(
        string code,
        CashFlowSection section,
        CashFlowMethod method = CashFlowMethod.Indirect)
        => new(code, method, section, code, 1, false);

    private sealed class Fixture
    {
        public Fixture()
        {
            Uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
            Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Repository.Setup(x => x.CreateAsync(It.IsAny<Account>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Repository.Setup(x => x.UpdateAsync(It.IsAny<Account>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Repository.Setup(x => x.SetActiveAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Repository.Setup(x => x.MarkForDeletionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Repository.Setup(x => x.UnmarkForDeletionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Audit.Setup(x => x.WriteAsync(
                    It.IsAny<AuditEntityKind>(),
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<AuditFieldChange>?>(),
                    It.IsAny<object?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            Sut = new ChartOfAccountsManagementService(
                Uow.Object,
                Repository.Object,
                CashFlowLines.Object,
                Audit.Object,
                NullLogger<ChartOfAccountsManagementService>.Instance);
        }

        public Mock<IUnitOfWork> Uow { get; } = new(MockBehavior.Loose);
        public Mock<IChartOfAccountsRepository> Repository { get; } = new(MockBehavior.Loose);
        public Mock<ICashFlowLineRepository> CashFlowLines { get; } = new(MockBehavior.Loose);
        public Mock<IAuditLogService> Audit { get; } = new(MockBehavior.Loose);
        public ChartOfAccountsManagementService Sut { get; }
    }
}
