using FluentAssertions;
using Moq;
using NGB.Accounting.Posting;
using NGB.AgencyBilling.Runtime.Policy;
using NGB.AgencyBilling.Runtime.Posting;
using NGB.AgencyBilling.Runtime.Tests.Infrastructure;
using NGB.Core.Dimensions;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Runtime.Dimensions;
using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Tests.Posting;

public sealed class AgencyBillingPostingHandlersFullCoverageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomerPaymentAccounting_UsesPolicyOrOverrideCachesInvoicesAndSkipsNonPositive(bool useOverride)
    {
        var document = AgencyBillingTestData.CreateDocument(AgencyBillingCodes.CustomerPayment);
        var invoiceId = Guid.CreateVersion7();
        var cash = AgencyBillingTestData.CreateAccount();
        var overrideCash = AgencyBillingTestData.CreateAccount();
        var receivable = AgencyBillingTestData.CreateAccount();
        var policy = Policy(cashAccountId: cash.Id, arAccountId: receivable.Id);
        var readers = new AgencyBillingTestData.DocumentReadersStub
        {
            CustomerPaymentHead = AgencyBillingTestData.ValidCustomerPaymentHead(
                documentId: document.Id,
                cashAccountId: useOverride ? overrideCash.Id : null),
            CustomerPaymentApplies =
            [
                AgencyBillingTestData.ValidCustomerPaymentApply(document.Id, 1, invoiceId, 12.34567m),
                AgencyBillingTestData.ValidCustomerPaymentApply(document.Id, 2, invoiceId, 0m)
            ],
            SalesInvoiceHead = AgencyBillingTestData.ValidSalesInvoiceHead(documentId: invoiceId),
        };
        var chart = AgencyBillingTestData.CreateChart(cash, overrideCash, receivable);
        var (context, posts) = PostingContext(chart);
        var handler = new CustomerPaymentPostingHandler(readers, PolicyReader(policy));

        handler.TypeCode.Should().Be(AgencyBillingCodes.CustomerPayment);
        await handler.BuildEntriesAsync(document, context.Object, CancellationToken.None);

        posts.Should().ContainSingle();
        posts[0].Amount.Should().Be(12.3457m);
        posts[0].Debit.Id.Should().Be(useOverride ? overrideCash.Id : cash.Id);
    }

    [Fact]
    public async Task Posting_handlers_reject_missing_batch_head_rows()
    {
        var paymentDocument = AgencyBillingTestData.CreateDocument(AgencyBillingCodes.CustomerPayment);
        var invoiceId = Guid.CreateVersion7();
        var paymentReaders = new AgencyBillingTestData.DocumentReadersStub
        {
            CustomerPaymentHead = AgencyBillingTestData.ValidCustomerPaymentHead(paymentDocument.Id),
            CustomerPaymentApplies = [AgencyBillingTestData.ValidCustomerPaymentApply(paymentDocument.Id, 1, invoiceId, 1m)],
            OmitBatchSalesInvoiceHeads = true
        };
        var policy = Policy();
        var chart = AgencyBillingTestData.CreateChart(
            AgencyBillingTestData.CreateAccount(policy.CashAccountId),
            AgencyBillingTestData.CreateAccount(policy.AccountsReceivableAccountId));
        var (postingContext, _) = PostingContext(chart);

        await ((Func<Task>)(() => new CustomerPaymentPostingHandler(paymentReaders, PolicyReader(policy))
                .BuildEntriesAsync(paymentDocument, postingContext.Object, default)))
            .Should().ThrowAsync<NgbInvariantViolationException>();

        var registers = AllOperationalRegisters(policy);
        await ((Func<Task>)(() => new CustomerPaymentOperationalRegisterPostingHandler(
                    paymentReaders, PolicyReader(policy), RegisterRepository(registers).Object, DimensionSets().Object)
                .BuildMovementsAsync(paymentDocument, Mock.Of<IOperationalRegisterMovementsBuilder>(), default)))
            .Should().ThrowAsync<NgbInvariantViolationException>();

        var invoiceDocument = AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice);
        var timesheetId = Guid.CreateVersion7();
        var invoiceReaders = new AgencyBillingTestData.DocumentReadersStub
        {
            SalesInvoiceHead = AgencyBillingTestData.ValidSalesInvoiceHead(invoiceDocument.Id),
            SalesInvoiceLines = [AgencyBillingTestData.ValidSalesInvoiceLine(
                invoiceDocument.Id, sourceTimesheetId: timesheetId, lineAmount: 1m)],
            OmitBatchTimesheetHeads = true
        };
        await ((Func<Task>)(() => new SalesInvoiceOperationalRegisterPostingHandler(
                    invoiceReaders, PolicyReader(policy), RegisterRepository(registers).Object, DimensionSets().Object)
                .BuildMovementsAsync(invoiceDocument, Mock.Of<IOperationalRegisterMovementsBuilder>(), default)))
            .Should().ThrowAsync<NgbInvariantViolationException>();
    }

    [Fact]
    public async Task SalesInvoiceAccounting_CoversHeadLinesAndNonPositiveBoundary()
    {
        var document = AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice);
        var receivable = AgencyBillingTestData.CreateAccount();
        var revenue = AgencyBillingTestData.CreateAccount();
        var chart = AgencyBillingTestData.CreateChart(receivable, revenue);
        var policy = Policy(arAccountId: receivable.Id, revenueAccountId: revenue.Id);

        var headReaders = new AgencyBillingTestData.DocumentReadersStub
        {
            SalesInvoiceHead = AgencyBillingTestData.ValidSalesInvoiceHead(document.Id, amount: 10.12345m),
            SalesInvoiceLines = [],
        };
        var (headContext, headPosts) = PostingContext(chart);
        var headHandler = new SalesInvoicePostingHandler(headReaders, PolicyReader(policy));
        headHandler.TypeCode.Should().Be(AgencyBillingCodes.SalesInvoice);
        await headHandler.BuildEntriesAsync(document, headContext.Object, CancellationToken.None);
        headPosts.Should().ContainSingle().Which.Amount.Should().Be(10.1235m);

        var lineReaders = new AgencyBillingTestData.DocumentReadersStub
        {
            SalesInvoiceHead = AgencyBillingTestData.ValidSalesInvoiceHead(document.Id),
            SalesInvoiceLines = [AgencyBillingTestData.ValidSalesInvoiceLine(document.Id, lineAmount: 20.12345m)],
        };
        var (lineContext, linePosts) = PostingContext(chart);
        await new SalesInvoicePostingHandler(lineReaders, PolicyReader(policy))
            .BuildEntriesAsync(document, lineContext.Object, CancellationToken.None);
        linePosts.Should().ContainSingle().Which.Amount.Should().Be(20.1235m);

        var zeroReaders = new AgencyBillingTestData.DocumentReadersStub
        {
            SalesInvoiceHead = AgencyBillingTestData.ValidSalesInvoiceHead(document.Id),
            SalesInvoiceLines = [AgencyBillingTestData.ValidSalesInvoiceLine(document.Id, lineAmount: 0m, quantityHours: 0m)],
        };
        var (zeroContext, zeroPosts) = PostingContext(chart);
        await new SalesInvoicePostingHandler(zeroReaders, PolicyReader(policy))
            .BuildEntriesAsync(document, zeroContext.Object, CancellationToken.None);
        zeroPosts.Should().BeEmpty();
    }

    [Fact]
    public async Task CustomerPaymentOperational_RejectsEachMissingRegister()
    {
        var policy = Policy();
        var readers = new AgencyBillingTestData.DocumentReadersStub();
        var document = AgencyBillingTestData.CreateDocument(AgencyBillingCodes.CustomerPayment);

        var missingProject = new CustomerPaymentOperationalRegisterPostingHandler(
            readers, PolicyReader(policy), RegisterRepository([]).Object, DimensionSets().Object);
        var first = () => missingProject.BuildMovementsAsync(document, Mock.Of<IOperationalRegisterMovementsBuilder>(), CancellationToken.None);
        await first.Should().ThrowAsync<NgbConfigurationViolationException>();

        var projectRegister = AgencyBillingTestData.Register(policy.ProjectBillingStatusOperationalRegisterId);
        var missingAr = new CustomerPaymentOperationalRegisterPostingHandler(
            readers, PolicyReader(policy), RegisterRepository([projectRegister]).Object, DimensionSets().Object);
        var second = () => missingAr.BuildMovementsAsync(document, Mock.Of<IOperationalRegisterMovementsBuilder>(), CancellationToken.None);
        await second.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task CustomerPaymentOperational_CachesInvoicesAddsTwoMovementsAndSkipsZero()
    {
        var document = AgencyBillingTestData.CreateDocument(AgencyBillingCodes.CustomerPayment);
        var invoiceId = Guid.CreateVersion7();
        var policy = Policy();
        var readers = new AgencyBillingTestData.DocumentReadersStub
        {
            CustomerPaymentHead = AgencyBillingTestData.ValidCustomerPaymentHead(document.Id),
            CustomerPaymentApplies =
            [
                AgencyBillingTestData.ValidCustomerPaymentApply(document.Id, 1, invoiceId, 25m),
                AgencyBillingTestData.ValidCustomerPaymentApply(document.Id, 2, invoiceId, 0m)
            ],
            SalesInvoiceHead = AgencyBillingTestData.ValidSalesInvoiceHead(documentId: invoiceId),
        };
        var registers = new[]
        {
            AgencyBillingTestData.Register(policy.ProjectBillingStatusOperationalRegisterId, "billing"),
            AgencyBillingTestData.Register(policy.ArOpenItemsOperationalRegisterId, "open-items")
        };
        var movements = new List<(string Code, OperationalRegisterMovement Movement)>();
        var builder = MovementBuilder(movements);
        var handler = new CustomerPaymentOperationalRegisterPostingHandler(
            readers, PolicyReader(policy), RegisterRepository(registers).Object, DimensionSets().Object);

        handler.TypeCode.Should().Be(AgencyBillingCodes.CustomerPayment);
        await handler.BuildMovementsAsync(document, builder.Object, CancellationToken.None);

        movements.Should().HaveCount(2);
        movements.SelectMany(item => item.Movement.Resources.Values).Should().Contain(25m).And.Contain(-25m);
    }

    [Fact]
    public async Task SalesInvoiceOperational_RejectsEachMissingRegister()
    {
        var policy = Policy();
        var readers = new AgencyBillingTestData.DocumentReadersStub();
        var document = AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice);
        var unbilled = AgencyBillingTestData.Register(policy.UnbilledTimeOperationalRegisterId);
        var billing = AgencyBillingTestData.Register(policy.ProjectBillingStatusOperationalRegisterId);

        foreach (var available in new[]
                 {
                     Array.Empty<OperationalRegisterAdminItem>(),
                     new[] { unbilled },
                     new[] { unbilled, billing }
                 })
        {
            var handler = new SalesInvoiceOperationalRegisterPostingHandler(
                readers, PolicyReader(policy), RegisterRepository(available).Object, DimensionSets().Object);
            var act = () => handler.BuildMovementsAsync(document, Mock.Of<IOperationalRegisterMovementsBuilder>(), CancellationToken.None);
            await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        }
    }

    [Fact]
    public async Task SalesInvoiceOperational_CoversSourceBoundariesCachingAndZeroTotal()
    {
        var document = AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice);
        var sourceId = Guid.CreateVersion7();
        var policy = Policy();
        var registers = AllOperationalRegisters(policy);
        var readers = new AgencyBillingTestData.DocumentReadersStub
        {
            SalesInvoiceHead = AgencyBillingTestData.ValidSalesInvoiceHead(document.Id),
            SalesInvoiceLines =
            [
                AgencyBillingTestData.ValidSalesInvoiceLine(document.Id, 1, sourceTimesheetId: null, lineAmount: 1m),
                AgencyBillingTestData.ValidSalesInvoiceLine(document.Id, 2, sourceTimesheetId: Guid.Empty, lineAmount: 2m),
                AgencyBillingTestData.ValidSalesInvoiceLine(document.Id, 3, sourceTimesheetId: sourceId, lineAmount: 3m),
                AgencyBillingTestData.ValidSalesInvoiceLine(document.Id, 4, sourceTimesheetId: sourceId, lineAmount: 4m)
            ],
            TimesheetHead = AgencyBillingTestData.ValidTimesheetHead(documentId: sourceId),
        };
        var movements = new List<(string Code, OperationalRegisterMovement Movement)>();
        var handler = new SalesInvoiceOperationalRegisterPostingHandler(
            readers, PolicyReader(policy), RegisterRepository(registers).Object, DimensionSets().Object);

        handler.TypeCode.Should().Be(AgencyBillingCodes.SalesInvoice);
        await handler.BuildMovementsAsync(document, MovementBuilder(movements).Object, CancellationToken.None);

        movements.Should().HaveCount(4);
        movements.Last().Movement.Resources["amount"].Should().Be(10m);

        var zeroReaders = new AgencyBillingTestData.DocumentReadersStub
        {
            SalesInvoiceHead = AgencyBillingTestData.ValidSalesInvoiceHead(document.Id),
            SalesInvoiceLines = [AgencyBillingTestData.ValidSalesInvoiceLine(document.Id, lineAmount: 0m, quantityHours: 0m)],
        };
        var zeroMovements = new List<(string Code, OperationalRegisterMovement Movement)>();
        await new SalesInvoiceOperationalRegisterPostingHandler(
                zeroReaders, PolicyReader(policy), RegisterRepository(registers).Object, DimensionSets().Object)
            .BuildMovementsAsync(document, MovementBuilder(zeroMovements).Object, CancellationToken.None);
        zeroMovements.Should().BeEmpty();
    }

    [Fact]
    public async Task TimesheetOperational_RejectsEachMissingRegister()
    {
        var policy = Policy();
        var readers = new AgencyBillingTestData.DocumentReadersStub();
        var document = AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet);

        var first = new TimesheetOperationalRegisterPostingHandler(
            readers, PolicyReader(policy), RegisterRepository([]).Object, DimensionSets().Object);
        Func<Task> firstAct = () => first.BuildMovementsAsync(
            document, Mock.Of<IOperationalRegisterMovementsBuilder>(), CancellationToken.None);
        await firstAct.Should().ThrowAsync<NgbConfigurationViolationException>();

        var projectTime = AgencyBillingTestData.Register(policy.ProjectTimeLedgerOperationalRegisterId);
        var second = new TimesheetOperationalRegisterPostingHandler(
            readers, PolicyReader(policy), RegisterRepository([projectTime]).Object, DimensionSets().Object);
        Func<Task> secondAct = () => second.BuildMovementsAsync(
            document, Mock.Of<IOperationalRegisterMovementsBuilder>(), CancellationToken.None);
        await secondAct.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task TimesheetOperational_AddsProjectTimeForAllAndUnbilledOnlyForBillable()
    {
        var document = AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet);
        var policy = Policy();
        var readers = new AgencyBillingTestData.DocumentReadersStub
        {
            TimesheetHead = AgencyBillingTestData.ValidTimesheetHead(document.Id),
            TimesheetLines =
            [
                AgencyBillingTestData.ValidTimesheetLine(document.Id, 1, billable: true),
                AgencyBillingTestData.ValidTimesheetLine(document.Id, 2, billable: false)
            ],
        };
        var registers = new[]
        {
            AgencyBillingTestData.Register(policy.ProjectTimeLedgerOperationalRegisterId, "project-time"),
            AgencyBillingTestData.Register(policy.UnbilledTimeOperationalRegisterId, "unbilled")
        };
        var movements = new List<(string Code, OperationalRegisterMovement Movement)>();
        var handler = new TimesheetOperationalRegisterPostingHandler(
            readers, PolicyReader(policy), RegisterRepository(registers).Object, DimensionSets().Object);

        handler.TypeCode.Should().Be(AgencyBillingCodes.Timesheet);
        await handler.BuildMovementsAsync(document, MovementBuilder(movements).Object, CancellationToken.None);

        movements.Should().HaveCount(3);
        movements.Count(item => item.Code == "project-time").Should().Be(2);
        movements.Count(item => item.Code == "unbilled").Should().Be(1);
    }

    private static AgencyBillingAccountingPolicy Policy(
        Guid? cashAccountId = null,
        Guid? arAccountId = null,
        Guid? revenueAccountId = null) =>
        new(
            Guid.CreateVersion7(),
            cashAccountId ?? Guid.CreateVersion7(),
            arAccountId ?? Guid.CreateVersion7(),
            revenueAccountId ?? Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7());

    private static IAgencyBillingAccountingPolicyReader PolicyReader(AgencyBillingAccountingPolicy policy)
    {
        var reader = new Mock<IAgencyBillingAccountingPolicyReader>(MockBehavior.Strict);
        reader.Setup(x => x.GetRequiredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(policy);
        return reader.Object;
    }

    private static (Mock<IAccountingPostingContext> Context, List<PostCall> Posts) PostingContext(
        Accounting.Accounts.ChartOfAccounts chart)
    {
        var posts = new List<PostCall>();
        var context = new Mock<IAccountingPostingContext>(MockBehavior.Strict);
        context.Setup(x => x.GetChartOfAccountsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(chart);
        context.Setup(x => x.Post(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<NGB.Accounting.Accounts.Account>(),
                It.IsAny<NGB.Accounting.Accounts.Account>(), It.IsAny<decimal>(), It.IsAny<DimensionBag>(),
                It.IsAny<DimensionBag>(), It.IsAny<bool>()))
            .Callback<Guid, DateTime, NGB.Accounting.Accounts.Account, NGB.Accounting.Accounts.Account, decimal,
                DimensionBag?, DimensionBag?, bool>((_, _, debit, credit, amount, _, _, _) =>
                posts.Add(new PostCall(debit, credit, amount)));
        return (context, posts);
    }

    private static Mock<IOperationalRegisterRepository> RegisterRepository(
        IEnumerable<OperationalRegisterAdminItem> registers)
    {
        var byId = registers.ToDictionary(register => register.RegisterId);
        var repository = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => byId.GetValueOrDefault(id));
        return repository;
    }

    private static Mock<IDimensionSetService> DimensionSets()
    {
        var service = new Mock<IDimensionSetService>(MockBehavior.Strict);
        service.Setup(x => x.GetOrCreateIdsAsync(It.IsAny<IReadOnlyList<DimensionBag>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DimensionBag> bags, CancellationToken _) =>
                bags.Select(static _ => Guid.CreateVersion7()).ToArray());
        return service;
    }

    private static Mock<IOperationalRegisterMovementsBuilder> MovementBuilder(
        ICollection<(string Code, OperationalRegisterMovement Movement)> movements)
    {
        var builder = new Mock<IOperationalRegisterMovementsBuilder>(MockBehavior.Strict);
        builder.Setup(x => x.Add(It.IsAny<string>(), It.IsAny<OperationalRegisterMovement>()))
            .Callback<string, OperationalRegisterMovement>((code, movement) => movements.Add((code, movement)));
        return builder;
    }

    private static IReadOnlyList<OperationalRegisterAdminItem> AllOperationalRegisters(
        AgencyBillingAccountingPolicy policy) =>
        [
            AgencyBillingTestData.Register(policy.UnbilledTimeOperationalRegisterId, "unbilled"),
            AgencyBillingTestData.Register(policy.ProjectBillingStatusOperationalRegisterId, "billing"),
            AgencyBillingTestData.Register(policy.ArOpenItemsOperationalRegisterId, "open-items")
        ];

    private sealed record PostCall(
        Accounting.Accounts.Account Debit,
        Accounting.Accounts.Account Credit,
        decimal Amount);
}
