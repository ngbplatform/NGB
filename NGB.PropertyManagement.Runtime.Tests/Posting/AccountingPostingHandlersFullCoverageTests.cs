using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.Posting;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Posting;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Posting;

public sealed class AccountingPostingHandlersFullCoverageTests
{
    private static readonly DateOnly Day = new(2026, 8, 16);

    [Fact]
    public async Task Simple_late_fee_and_rent_handlers_post_balanced_dimensioned_entries()
    {
        var late = new Fixture();
        late.Readers.Setup(x => x.ReadLateFeeChargeHeadAsync(late.DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmLateFeeChargeHead(
                late.DocumentId, late.PartyId, late.PropertyId, late.LeaseId, Day, 12m, null));
        var lateHandler = new LateFeeChargePostingHandler(late.Readers.Object, late.PolicyReader.Object);
        lateHandler.TypeCode.Should().Be(PropertyManagementCodes.LateFeeCharge);
        await lateHandler.BuildEntriesAsync(late.Document(PropertyManagementCodes.LateFeeCharge), late.Context.Object, default);
        late.Posts.Should().ContainSingle();
        late.Posts[0].DebitDimensions!.Count.Should().Be(3);
        late.Posts[0].CreditDimensions.Should().BeSameAs(late.Posts[0].DebitDimensions);

        var rent = new Fixture();
        rent.Readers.Setup(x => x.ReadRentChargeHeadAsync(rent.DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmRentChargeHead(
                rent.DocumentId, rent.LeaseId, rent.PartyId, rent.PropertyId, Day, Day, Day, 12m, null));
        var rentHandler = new RentChargePostingHandler(rent.Readers.Object, rent.PolicyReader.Object);
        rentHandler.TypeCode.Should().Be(PropertyManagementCodes.RentCharge);
        await rentHandler.BuildEntriesAsync(rent.Document(PropertyManagementCodes.RentCharge), rent.Context.Object, default);
        rent.Posts.Should().ContainSingle();
        rent.Posts[0].DebitDimensions!.Count.Should().Be(3);
    }

    [Fact]
    public async Task Payable_charge_rejects_null_and_empty_debit_account_and_posts_valid_mapping()
    {
        await AssertPayableChargeAccountFailureAsync(null);
        await AssertPayableChargeAccountFailureAsync(Guid.Empty);

        var fixture = new Fixture();
        fixture.SetupPayableCharge(fixture.ChargeAccountId, amount: 12m);
        var sut = new PayableChargePostingHandler(fixture.Readers.Object, fixture.PolicyReader.Object);
        sut.TypeCode.Should().Be(PropertyManagementCodes.PayableCharge);

        await sut.BuildEntriesAsync(fixture.Document(PropertyManagementCodes.PayableCharge), fixture.Context.Object, default);

        fixture.Posts.Should().ContainSingle();
        fixture.Posts[0].Debit.Id.Should().Be(fixture.ChargeAccountId);
        fixture.Posts[0].Credit.Id.Should().Be(fixture.Policy.AccountsPayableVendorsAccountId);
        fixture.Posts[0].DebitDimensions!.Count.Should().Be(2);
    }

    [Fact]
    public async Task Payable_credit_memo_rejects_account_and_amount_boundaries_and_posts_valid_mapping()
    {
        await AssertPayableCreditMemoFailureAsync(null, 12m, typeof(NgbConfigurationViolationException));
        await AssertPayableCreditMemoFailureAsync(Guid.Empty, 12m, typeof(NgbConfigurationViolationException));
        await AssertPayableCreditMemoFailureAsync(Guid.CreateVersion7(), 0m, exceptionType: null);

        var fixture = new Fixture();
        fixture.SetupPayableCreditMemo(fixture.ChargeAccountId, 12m);
        var sut = new PayableCreditMemoPostingHandler(fixture.Readers.Object, fixture.PolicyReader.Object);
        sut.TypeCode.Should().Be(PropertyManagementCodes.PayableCreditMemo);

        await sut.BuildEntriesAsync(fixture.Document(PropertyManagementCodes.PayableCreditMemo), fixture.Context.Object, default);

        fixture.Posts.Should().ContainSingle();
        fixture.Posts[0].Debit.Id.Should().Be(fixture.Policy.AccountsPayableVendorsAccountId);
        fixture.Posts[0].Credit.Id.Should().Be(fixture.ChargeAccountId);
    }

    [Fact]
    public async Task Receivable_charge_rejects_null_and_empty_credit_account_and_posts_valid_mapping()
    {
        await AssertReceivableChargeAccountFailureAsync(null);
        await AssertReceivableChargeAccountFailureAsync(Guid.Empty);

        var fixture = new Fixture();
        fixture.SetupReceivableCharge(fixture.ChargeAccountId);
        var sut = new ReceivableChargePostingHandler(fixture.Readers.Object, fixture.PolicyReader.Object);
        sut.TypeCode.Should().Be(PropertyManagementCodes.ReceivableCharge);

        await sut.BuildEntriesAsync(fixture.Document(PropertyManagementCodes.ReceivableCharge), fixture.Context.Object, default);

        fixture.Posts.Should().ContainSingle();
        fixture.Posts[0].Debit.Id.Should().Be(fixture.Policy.AccountsReceivableTenantsAccountId);
        fixture.Posts[0].Credit.Id.Should().Be(fixture.ChargeAccountId);
    }

    [Fact]
    public async Task Receivable_credit_memo_requires_classification_and_valid_credit_account_then_posts()
    {
        var noClassification = new Fixture();
        noClassification.SetupReceivableCreditMemo(chargeTypeId: null, creditAccountId: null);
        var noClassificationSut = new ReceivableCreditMemoPostingHandler(
            noClassification.Readers.Object, noClassification.PolicyReader.Object);
        await ((Func<Task>)(() => noClassificationSut.BuildEntriesAsync(
                noClassification.Document(PropertyManagementCodes.ReceivableCreditMemo),
                noClassification.Context.Object,
                default)))
            .Should().ThrowAsync<Exception>();

        await AssertReceivableCreditMemoAccountFailureAsync(null);
        await AssertReceivableCreditMemoAccountFailureAsync(Guid.Empty);

        var fixture = new Fixture();
        fixture.SetupReceivableCreditMemo(fixture.ChargeTypeId, fixture.ChargeAccountId);
        var sut = new ReceivableCreditMemoPostingHandler(fixture.Readers.Object, fixture.PolicyReader.Object);
        sut.TypeCode.Should().Be(PropertyManagementCodes.ReceivableCreditMemo);
        await sut.BuildEntriesAsync(fixture.Document(PropertyManagementCodes.ReceivableCreditMemo), fixture.Context.Object, default);

        fixture.Posts.Should().ContainSingle();
        fixture.Posts[0].Debit.Id.Should().Be(fixture.ChargeAccountId);
        fixture.Posts[0].Credit.Id.Should().Be(fixture.Policy.AccountsReceivableTenantsAccountId);
    }

    [Fact]
    public async Task Every_payment_handler_covers_selected_default_fallback_and_deleted_bank_accounts()
    {
        foreach (var paymentCase in PaymentCases())
        {
            var selected = new Fixture();
            var selectedBankId = Guid.CreateVersion7();
            selected.BankAccounts.Setup(x => x.GetRequiredAsync(selectedBankId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PropertyManagementBankAccount(
                    selectedBankId, "Selected", selected.BankAccountGlId, false, false));
            var selectedHandler = paymentCase.Create(selected, selectedBankId);
            selectedHandler.TypeCode.Should().Be(paymentCase.TypeCode);
            await selectedHandler.BuildEntriesAsync(selected.Document(paymentCase.TypeCode), selected.Context.Object, default);
            selected.Posts.Should().ContainSingle();
            paymentCase.AssertCashAccount(selected.Posts[0], selected.BankAccountGlId);

            var deleted = new Fixture();
            var deletedBankId = Guid.CreateVersion7();
            deleted.BankAccounts.Setup(x => x.GetRequiredAsync(deletedBankId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PropertyManagementBankAccount(
                    deletedBankId, "Deleted", deleted.BankAccountGlId, false, true));
            var deletedHandler = paymentCase.Create(deleted, deletedBankId);
            var deletedAct = () => deletedHandler.BuildEntriesAsync(
                deleted.Document(paymentCase.TypeCode), deleted.Context.Object, default);
            await deletedAct.Should().ThrowAsync<Exception>();

            var defaultBank = new Fixture();
            defaultBank.BankAccounts.Setup(x => x.TryGetDefaultAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PropertyManagementBankAccount(
                    Guid.CreateVersion7(), "Default", defaultBank.BankAccountGlId, true, false));
            var defaultHandler = paymentCase.Create(defaultBank, null);
            await defaultHandler.BuildEntriesAsync(defaultBank.Document(paymentCase.TypeCode), defaultBank.Context.Object, default);
            paymentCase.AssertCashAccount(defaultBank.Posts.Single(), defaultBank.BankAccountGlId);

            var fallback = new Fixture();
            fallback.BankAccounts.Setup(x => x.TryGetDefaultAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((PropertyManagementBankAccount?)null);
            var fallbackHandler = paymentCase.Create(fallback, null);
            await fallbackHandler.BuildEntriesAsync(fallback.Document(paymentCase.TypeCode), fallback.Context.Object, default);
            paymentCase.AssertCashAccount(fallback.Posts.Single(), fallback.Policy.CashAccountId);
        }
    }

    private static async Task AssertPayableChargeAccountFailureAsync(Guid? accountId)
    {
        var fixture = new Fixture();
        fixture.SetupPayableCharge(accountId, 12m);
        var sut = new PayableChargePostingHandler(fixture.Readers.Object, fixture.PolicyReader.Object);
        var act = () => sut.BuildEntriesAsync(
            fixture.Document(PropertyManagementCodes.PayableCharge), fixture.Context.Object, default);
        await act.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    private static async Task AssertPayableCreditMemoFailureAsync(Guid? accountId, decimal amount, Type? exceptionType)
    {
        var fixture = new Fixture();
        fixture.SetupPayableCreditMemo(accountId, amount);
        var sut = new PayableCreditMemoPostingHandler(fixture.Readers.Object, fixture.PolicyReader.Object);
        var act = () => sut.BuildEntriesAsync(
            fixture.Document(PropertyManagementCodes.PayableCreditMemo), fixture.Context.Object, default);
        if (exceptionType == typeof(NgbConfigurationViolationException))
            await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        else
            await act.Should().ThrowAsync<Exception>();
    }

    private static async Task AssertReceivableChargeAccountFailureAsync(Guid? accountId)
    {
        var fixture = new Fixture();
        fixture.SetupReceivableCharge(accountId);
        var sut = new ReceivableChargePostingHandler(fixture.Readers.Object, fixture.PolicyReader.Object);
        var act = () => sut.BuildEntriesAsync(
            fixture.Document(PropertyManagementCodes.ReceivableCharge), fixture.Context.Object, default);
        await act.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    private static async Task AssertReceivableCreditMemoAccountFailureAsync(Guid? accountId)
    {
        var fixture = new Fixture();
        fixture.SetupReceivableCreditMemo(fixture.ChargeTypeId, accountId);
        var sut = new ReceivableCreditMemoPostingHandler(fixture.Readers.Object, fixture.PolicyReader.Object);
        var act = () => sut.BuildEntriesAsync(
            fixture.Document(PropertyManagementCodes.ReceivableCreditMemo), fixture.Context.Object, default);
        await act.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    private static IReadOnlyList<PaymentCase> PaymentCases()
        =>
        [
            new(
                PropertyManagementCodes.PayablePayment,
                (fixture, bankAccountId) =>
                {
                    fixture.Readers.Setup(x => x.ReadPayablePaymentHeadAsync(
                            fixture.DocumentId, It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new PmPayablePaymentHead(
                            fixture.DocumentId, fixture.PartyId, fixture.PropertyId, bankAccountId, Day, 12m, null));
                    return new PayablePaymentPostingHandler(
                        fixture.Readers.Object, fixture.PolicyReader.Object, fixture.BankAccounts.Object);
                },
                (post, cash) => post.Credit.Id.Should().Be(cash)),
            new(
                PropertyManagementCodes.ReceivablePayment,
                (fixture, bankAccountId) =>
                {
                    fixture.Readers.Setup(x => x.ReadReceivablePaymentHeadAsync(
                            fixture.DocumentId, It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new PmReceivablePaymentHead(
                            fixture.DocumentId, fixture.PartyId, fixture.PropertyId, fixture.LeaseId,
                            bankAccountId, Day, 12m, null));
                    return new ReceivablePaymentPostingHandler(
                        fixture.Readers.Object, fixture.PolicyReader.Object, fixture.BankAccounts.Object);
                },
                (post, cash) => post.Debit.Id.Should().Be(cash)),
            new(
                PropertyManagementCodes.ReceivableReturnedPayment,
                (fixture, bankAccountId) =>
                {
                    fixture.Readers.Setup(x => x.ReadReceivableReturnedPaymentHeadAsync(
                            fixture.DocumentId, It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new PmReceivableReturnedPaymentHead(
                            fixture.DocumentId, fixture.PartyId, fixture.PropertyId, fixture.LeaseId,
                            Guid.CreateVersion7(), bankAccountId, Day, 12m, null));
                    return new ReceivableReturnedPaymentPostingHandler(
                        fixture.Readers.Object, fixture.PolicyReader.Object, fixture.BankAccounts.Object);
                },
                (post, cash) => post.Credit.Id.Should().Be(cash))
        ];

    private sealed record PaymentCase(
        string TypeCode,
        Func<Fixture, Guid?, IDocumentPostingHandler> Create,
        Action<PostCall, Guid> AssertCashAccount);

    private sealed record PostCall(
        Account Debit,
        Account Credit,
        decimal Amount,
        DimensionBag? DebitDimensions,
        DimensionBag? CreditDimensions);

    private sealed class Fixture
    {
        public Fixture()
        {
            Policy = new PropertyManagementAccountingPolicy(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7());
            PolicyReader.Setup(x => x.GetRequiredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Policy);
            Readers.Setup(x => x.ReadLeaseHeadAsync(LeaseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PmLeaseHead(LeaseId, PartyId, PropertyId, Day, null));

            var chart = new ChartOfAccounts();
            foreach (var id in new[]
                     {
                         Policy.CashAccountId,
                         Policy.AccountsReceivableTenantsAccountId,
                         Policy.AccountsPayableVendorsAccountId,
                         Policy.RentalIncomeAccountId,
                         Policy.LateFeeIncomeAccountId,
                         ChargeAccountId,
                         BankAccountGlId
                     }.Distinct())
            {
                chart.Add(new Account(id, id.ToString("N"), "Account", AccountType.Asset, StatementSection.Assets));
            }

            Context.Setup(x => x.GetChartOfAccountsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(chart);
            Context.Setup(x => x.Post(
                    It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<Account>(), It.IsAny<Account>(),
                    It.IsAny<decimal>(), It.IsAny<DimensionBag>(), It.IsAny<DimensionBag>(), It.IsAny<bool>()))
                .Callback<Guid, DateTime, Account, Account, decimal, DimensionBag?, DimensionBag?, bool>(
                    (_, _, debit, credit, amount, debitDimensions, creditDimensions, _) =>
                        Posts.Add(new PostCall(debit, credit, amount, debitDimensions, creditDimensions)));
        }

        public Guid DocumentId { get; } = Guid.CreateVersion7();
        public Guid PartyId { get; } = Guid.CreateVersion7();
        public Guid PropertyId { get; } = Guid.CreateVersion7();
        public Guid LeaseId { get; } = Guid.CreateVersion7();
        public Guid ChargeTypeId { get; } = Guid.CreateVersion7();
        public Guid ChargeAccountId { get; } = Guid.CreateVersion7();
        public Guid BankAccountGlId { get; } = Guid.CreateVersion7();
        public PropertyManagementAccountingPolicy Policy { get; }
        public List<PostCall> Posts { get; } = [];
        public Mock<IPropertyManagementDocumentReaders> Readers { get; } = new(MockBehavior.Strict);
        public Mock<IPropertyManagementAccountingPolicyReader> PolicyReader { get; } = new(MockBehavior.Strict);
        public Mock<IPropertyManagementBankAccountReader> BankAccounts { get; } = new(MockBehavior.Strict);
        public Mock<IAccountingPostingContext> Context { get; } = new(MockBehavior.Strict);

        public void SetupPayableCharge(Guid? debitAccountId, decimal amount)
        {
            Readers.Setup(x => x.ReadPayableChargeHeadAsync(DocumentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PmPayableChargeHead(
                    DocumentId, PartyId, PropertyId, ChargeTypeId, Day, amount, null, null));
            Readers.Setup(x => x.ReadPayableChargeTypeHeadAsync(ChargeTypeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PmPayableChargeTypeHead(ChargeTypeId, "Expense", debitAccountId));
        }

        public void SetupPayableCreditMemo(Guid? debitAccountId, decimal amount)
        {
            Readers.Setup(x => x.ReadPayableCreditMemoHeadAsync(DocumentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PmPayableCreditMemoHead(
                    DocumentId, PartyId, PropertyId, ChargeTypeId, Day, amount, null));
            Readers.Setup(x => x.ReadPayableChargeTypeHeadAsync(ChargeTypeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PmPayableChargeTypeHead(ChargeTypeId, "Expense", debitAccountId));
        }

        public void SetupReceivableCharge(Guid? creditAccountId)
        {
            Readers.Setup(x => x.ReadReceivableChargeHeadAsync(DocumentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PmReceivableChargeHead(
                    DocumentId, PartyId, PropertyId, LeaseId, ChargeTypeId, Day, 12m, null));
            Readers.Setup(x => x.ReadChargeTypeHeadAsync(ChargeTypeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PmChargeTypeHead(ChargeTypeId, "Income", creditAccountId));
        }

        public void SetupReceivableCreditMemo(Guid? chargeTypeId, Guid? creditAccountId)
        {
            Readers.Setup(x => x.ReadReceivableCreditMemoHeadAsync(DocumentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PmReceivableCreditMemoHead(
                    DocumentId, PartyId, PropertyId, LeaseId, chargeTypeId, Day, 12m, null));
            if (chargeTypeId is { } id)
            {
                Readers.Setup(x => x.ReadChargeTypeHeadAsync(id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new PmChargeTypeHead(id, "Income", creditAccountId));
            }
        }

        public DocumentRecord Document(string type)
            => new()
            {
                Id = DocumentId,
                TypeCode = type,
                DateUtc = Day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                Status = DocumentStatus.Posted
            };
    }
}
