using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.AgencyBilling.Documents;
using NGB.AgencyBilling.References;
using NGB.AgencyBilling.Runtime.Documents.Validation;
using NGB.AgencyBilling.Runtime.Policy;
using NGB.AgencyBilling.Runtime.Posting;
using NGB.AgencyBilling.Runtime.Tests.Infrastructure;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.OperationalRegisters;
using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Tests.Documents;

public sealed class SalesInvoicePostValidator_P0Tests
{
    [Fact]
    public async Task ValidateBeforePostAsync_When_Bound_To_Different_Type_Throws()
    {
        var harness = CreateSalesInvoiceHarness();

        Func<Task> act = () => harness.Sut.ValidateBeforePostAsync(
            AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet),
            CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Client_Is_Invalid_Throws()
    {
        var head = AgencyBillingTestData.ValidSalesInvoiceHead(contractId: null);
        var harness = CreateSalesInvoiceHarness(head: head, refs: ValidInvoiceReferences());

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice), CancellationToken.None));

        ex.ParamName.Should().Be("client_id");
        ex.Reason.Should().Be("Referenced client was not found.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Project_Does_Not_Belong_To_Client_Throws()
    {
        var head = AgencyBillingTestData.ValidSalesInvoiceHead(contractId: null);
        var serviceItemId = Guid.NewGuid();
        var refs = ValidInvoiceReferences(
            clientId: head.ClientId,
            projectId: head.ProjectId,
            projectClientId: Guid.NewGuid(),
            serviceItemId: serviceItemId);
        var line = AgencyBillingTestData.ValidSalesInvoiceLine(documentId: head.DocumentId, serviceItemId: serviceItemId);
        var harness = CreateSalesInvoiceHarness(head: head, lines: [line], refs: refs);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice), CancellationToken.None));

        ex.ParamName.Should().Be("project_id");
        ex.Reason.Should().Be("Selected project does not belong to the client specified in 'client_id'.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Contract_Is_Missing_Throws()
    {
        var contractId = Guid.NewGuid();
        var head = AgencyBillingTestData.ValidSalesInvoiceHead(contractId: contractId);
        var serviceItemId = Guid.NewGuid();
        var refs = ValidInvoiceReferences(clientId: head.ClientId, projectId: head.ProjectId, serviceItemId: serviceItemId);
        var line = AgencyBillingTestData.ValidSalesInvoiceLine(documentId: head.DocumentId, serviceItemId: serviceItemId);
        var harness = CreateSalesInvoiceHarness(
            head: head,
            lines: [line],
            refs: refs,
            documentsById: Map<DocumentRecord>());

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice), CancellationToken.None));

        ex.ParamName.Should().Be("contract_id");
        ex.Reason.Should().Be("Referenced client contract was not found.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Contract_Does_Not_Match_Client_And_Project_Throws()
    {
        var contractId = Guid.NewGuid();
        var head = AgencyBillingTestData.ValidSalesInvoiceHead(contractId: contractId);
        var serviceItemId = Guid.NewGuid();
        var refs = ValidInvoiceReferences(clientId: head.ClientId, projectId: head.ProjectId, serviceItemId: serviceItemId);
        var line = AgencyBillingTestData.ValidSalesInvoiceLine(documentId: head.DocumentId, serviceItemId: serviceItemId);
        var contract = AgencyBillingTestData.ValidClientContractHead(
            documentId: contractId,
            clientId: head.ClientId,
            projectId: Guid.NewGuid());
        var harness = CreateSalesInvoiceHarness(
            head: head,
            lines: [line],
            refs: refs,
            documentsById: Map(
                (contractId, AgencyBillingTestData.CreateDocument(AgencyBillingCodes.ClientContract, DocumentStatus.Posted, id: contractId))),
            contractsById: Map((contractId, contract)));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice), CancellationToken.None));

        ex.ParamName.Should().Be("contract_id");
        ex.Reason.Should().Be("Referenced client contract must belong to the same client and project as the invoice.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Lines_Are_Missing_Throws()
    {
        var head = AgencyBillingTestData.ValidSalesInvoiceHead(contractId: null);
        var harness = CreateSalesInvoiceHarness(head: head, lines: [], refs: ValidInvoiceReferences(head.ClientId, head.ProjectId));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice), CancellationToken.None));

        ex.ParamName.Should().Be("lines");
        ex.Reason.Should().Be("Sales Invoice must contain at least one line.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Service_Item_Is_Invalid_Throws()
    {
        var head = AgencyBillingTestData.ValidSalesInvoiceHead(contractId: null);
        var line = AgencyBillingTestData.ValidSalesInvoiceLine(documentId: head.DocumentId);
        var harness = CreateSalesInvoiceHarness(
            head: head,
            lines: [line],
            refs: ValidInvoiceReferences(head.ClientId, head.ProjectId));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice), CancellationToken.None));

        ex.ParamName.Should().Be("lines[0].service_item_id");
        ex.Reason.Should().Be("Referenced service item was not found.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateBeforePostAsync_When_Quantity_Hours_Are_Not_Positive_Throws(decimal quantityHours)
    {
        var head = AgencyBillingTestData.ValidSalesInvoiceHead(contractId: null);
        var serviceItemId = Guid.NewGuid();
        var line = AgencyBillingTestData.ValidSalesInvoiceLine(
            documentId: head.DocumentId,
            serviceItemId: serviceItemId,
            quantityHours: quantityHours,
            lineAmount: 160m);
        var harness = CreateSalesInvoiceHarness(
            head: head,
            lines: [line],
            refs: ValidInvoiceReferences(head.ClientId, head.ProjectId, serviceItemId));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice), CancellationToken.None));

        ex.ParamName.Should().Be("lines[0].quantity_hours");
        ex.Reason.Should().Be("Quantity Hours must be greater than zero.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task ValidateBeforePostAsync_When_Rate_Is_Not_Positive_Throws(decimal rate)
    {
        var head = AgencyBillingTestData.ValidSalesInvoiceHead(contractId: null);
        var serviceItemId = Guid.NewGuid();
        var line = AgencyBillingTestData.ValidSalesInvoiceLine(
            documentId: head.DocumentId,
            serviceItemId: serviceItemId,
            rate: rate,
            lineAmount: 0m);
        var harness = CreateSalesInvoiceHarness(
            head: head,
            lines: [line],
            refs: ValidInvoiceReferences(head.ClientId, head.ProjectId, serviceItemId));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice), CancellationToken.None));

        ex.ParamName.Should().Be("lines[0].rate");
        ex.Reason.Should().Be("Rate must be greater than zero.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Line_Amount_Does_Not_Match_Quantity_And_Rate_Throws()
    {
        var head = AgencyBillingTestData.ValidSalesInvoiceHead(contractId: null);
        var serviceItemId = Guid.NewGuid();
        var line = AgencyBillingTestData.ValidSalesInvoiceLine(
            documentId: head.DocumentId,
            serviceItemId: serviceItemId,
            quantityHours: 2.5m,
            rate: 160m,
            lineAmount: 401m);
        var harness = CreateSalesInvoiceHarness(
            head: head with { Amount = 401m },
            lines: [line],
            refs: ValidInvoiceReferences(head.ClientId, head.ProjectId, serviceItemId));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice), CancellationToken.None));

        ex.ParamName.Should().Be("lines[0].line_amount");
        ex.Reason.Should().Contain("Quantity Hours x Rate");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Source_Timesheet_Is_Missing_Throws()
    {
        var timesheetId = Guid.NewGuid();
        var head = AgencyBillingTestData.ValidSalesInvoiceHead(contractId: null);
        var serviceItemId = Guid.NewGuid();
        var line = AgencyBillingTestData.ValidSalesInvoiceLine(
            documentId: head.DocumentId,
            serviceItemId: serviceItemId,
            sourceTimesheetId: timesheetId);
        var harness = CreateSalesInvoiceHarness(
            head: head,
            lines: [line],
            refs: ValidInvoiceReferences(head.ClientId, head.ProjectId, serviceItemId));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice), CancellationToken.None));

        ex.ParamName.Should().Be("lines[0].source_timesheet_id");
        ex.Reason.Should().Be("Referenced source timesheet was not found.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Source_Timesheet_Is_Not_Posted_Throws()
    {
        var timesheetId = Guid.NewGuid();
        var head = AgencyBillingTestData.ValidSalesInvoiceHead(contractId: null);
        var serviceItemId = Guid.NewGuid();
        var line = AgencyBillingTestData.ValidSalesInvoiceLine(
            documentId: head.DocumentId,
            serviceItemId: serviceItemId,
            sourceTimesheetId: timesheetId);
        var harness = CreateSalesInvoiceHarness(
            head: head,
            lines: [line],
            refs: ValidInvoiceReferences(head.ClientId, head.ProjectId, serviceItemId),
            documentsById: Map(
                (timesheetId, AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet, DocumentStatus.Draft, id: timesheetId))));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice), CancellationToken.None));

        ex.ParamName.Should().Be("lines[0].source_timesheet_id");
        ex.Reason.Should().Be("Referenced source timesheet must be posted.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Source_Timesheet_Does_Not_Match_Client_And_Project_Throws()
    {
        var timesheetId = Guid.NewGuid();
        var head = AgencyBillingTestData.ValidSalesInvoiceHead(contractId: null);
        var serviceItemId = Guid.NewGuid();
        var line = AgencyBillingTestData.ValidSalesInvoiceLine(
            documentId: head.DocumentId,
            serviceItemId: serviceItemId,
            sourceTimesheetId: timesheetId);
        var timesheetHead = AgencyBillingTestData.ValidTimesheetHead(
            documentId: timesheetId,
            clientId: head.ClientId,
            projectId: Guid.NewGuid());
        var harness = CreateSalesInvoiceHarness(
            head: head,
            lines: [line],
            refs: ValidInvoiceReferences(head.ClientId, head.ProjectId, serviceItemId),
            documentsById: Map(
                (timesheetId, AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet, DocumentStatus.Posted, id: timesheetId))),
            timesheetHeadsById: Map((timesheetId, timesheetHead)));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice), CancellationToken.None));

        ex.ParamName.Should().Be("lines[0].source_timesheet_id");
        ex.Reason.Should().Be("Referenced source timesheet must belong to the same client and project as the invoice.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Amount_Does_Not_Equal_Line_Sum_Throws()
    {
        var head = AgencyBillingTestData.ValidSalesInvoiceHead(contractId: null, amount: 999m);
        var serviceItemId = Guid.NewGuid();
        var line = AgencyBillingTestData.ValidSalesInvoiceLine(documentId: head.DocumentId, serviceItemId: serviceItemId);
        var harness = CreateSalesInvoiceHarness(
            head: head,
            lines: [line],
            refs: ValidInvoiceReferences(head.ClientId, head.ProjectId, serviceItemId));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice), CancellationToken.None));

        ex.ParamName.Should().Be("amount");
        ex.Reason.Should().Be("Amount must equal the sum of invoice line amounts.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Invoice_Exceeds_Remaining_Billable_Hours_Throws()
    {
        var timesheetId = Guid.NewGuid();
        var serviceItemId = Guid.NewGuid();
        var head = AgencyBillingTestData.ValidSalesInvoiceHead(contractId: null, amount: 320m);
        var line = AgencyBillingTestData.ValidSalesInvoiceLine(
            documentId: head.DocumentId,
            serviceItemId: serviceItemId,
            sourceTimesheetId: timesheetId,
            quantityHours: 2m,
            rate: 160m,
            lineAmount: 320m);
        var timesheetHead = AgencyBillingTestData.ValidTimesheetHead(
            documentId: timesheetId,
            clientId: head.ClientId,
            projectId: head.ProjectId,
            totalHours: 8m,
            amount: 1280m);
        var timesheetLines = new[]
        {
            AgencyBillingTestData.ValidTimesheetLine(documentId: timesheetId, serviceItemId: serviceItemId, hours: 8m, lineAmount: 1280m)
        };
        var harness = CreateSalesInvoiceHarness(
            head: head,
            lines: [line],
            refs: ValidInvoiceReferences(head.ClientId, head.ProjectId, serviceItemId),
            documentsById: Map(
                (timesheetId, AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet, DocumentStatus.Posted, id: timesheetId))),
            timesheetHeadsById: Map((timesheetId, timesheetHead)),
            timesheetLinesById: Map<IReadOnlyList<AgencyBillingTimesheetLine>>((timesheetId, timesheetLines)),
            usageByTimesheetId: Map((timesheetId, new AgencyBillingTimesheetInvoiceUsage(6.5m, 600m))));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice), CancellationToken.None));

        ex.ParamName.Should().Be("lines");
        ex.Reason.Should().Contain("remaining billable hours");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Invoice_Exceeds_Remaining_Billable_Amount_Throws()
    {
        var timesheetId = Guid.NewGuid();
        var serviceItemId = Guid.NewGuid();
        var head = AgencyBillingTestData.ValidSalesInvoiceHead(contractId: null, amount: 320m);
        var line = AgencyBillingTestData.ValidSalesInvoiceLine(
            documentId: head.DocumentId,
            serviceItemId: serviceItemId,
            sourceTimesheetId: timesheetId,
            quantityHours: 2m,
            rate: 160m,
            lineAmount: 320m);
        var timesheetHead = AgencyBillingTestData.ValidTimesheetHead(
            documentId: timesheetId,
            clientId: head.ClientId,
            projectId: head.ProjectId,
            totalHours: 8m,
            amount: 1280m);
        var timesheetLines = new[]
        {
            AgencyBillingTestData.ValidTimesheetLine(documentId: timesheetId, serviceItemId: serviceItemId, hours: 8m, lineAmount: 1280m)
        };
        var harness = CreateSalesInvoiceHarness(
            head: head,
            lines: [line],
            refs: ValidInvoiceReferences(head.ClientId, head.ProjectId, serviceItemId),
            documentsById: Map(
                (timesheetId, AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet, DocumentStatus.Posted, id: timesheetId))),
            timesheetHeadsById: Map((timesheetId, timesheetHead)),
            timesheetLinesById: Map<IReadOnlyList<AgencyBillingTimesheetLine>>((timesheetId, timesheetLines)),
            usageByTimesheetId: Map((timesheetId, new AgencyBillingTimesheetInvoiceUsage(5m, 1000m))));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice), CancellationToken.None));

        ex.ParamName.Should().Be("lines");
        ex.Reason.Should().Contain("remaining billable amount");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Invoice_Is_Consistent_Passes_And_Locks_Source_Timesheet_Once()
    {
        var contractId = Guid.NewGuid();
        var timesheetId = Guid.NewGuid();
        var serviceItemId = Guid.NewGuid();
        var head = AgencyBillingTestData.ValidSalesInvoiceHead(contractId: contractId, amount: 1280m);
        var lines = new[]
        {
            AgencyBillingTestData.ValidSalesInvoiceLine(
                documentId: head.DocumentId,
                ordinal: 1,
                serviceItemId: serviceItemId,
                sourceTimesheetId: timesheetId,
                quantityHours: 3m,
                rate: 160m,
                lineAmount: 480m,
                description: "Workshop"),
            AgencyBillingTestData.ValidSalesInvoiceLine(
                documentId: head.DocumentId,
                ordinal: 2,
                serviceItemId: serviceItemId,
                sourceTimesheetId: timesheetId,
                quantityHours: 5m,
                rate: 160m,
                lineAmount: 800m,
                description: "Implementation")
        };
        var timesheetHead = AgencyBillingTestData.ValidTimesheetHead(
            documentId: timesheetId,
            clientId: head.ClientId,
            projectId: head.ProjectId,
            totalHours: 8m,
            amount: 1280m);
        var timesheetLines = new[]
        {
            AgencyBillingTestData.ValidTimesheetLine(
                documentId: timesheetId,
                serviceItemId: serviceItemId,
                hours: 8m,
                lineAmount: 1280m)
        };
        var contractHead = AgencyBillingTestData.ValidClientContractHead(
            documentId: contractId,
            clientId: head.ClientId,
            projectId: head.ProjectId,
            isActive: true);
        var harness = CreateSalesInvoiceHarness(
            head: head,
            lines: lines,
            refs: ValidInvoiceReferences(head.ClientId, head.ProjectId, serviceItemId),
            documentsById: Map(
                (contractId, AgencyBillingTestData.CreateDocument(AgencyBillingCodes.ClientContract, DocumentStatus.Posted, id: contractId)),
                (timesheetId, AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet, DocumentStatus.Posted, id: timesheetId))),
            contractsById: Map((contractId, contractHead)),
            timesheetHeadsById: Map((timesheetId, timesheetHead)),
            timesheetLinesById: Map<IReadOnlyList<AgencyBillingTimesheetLine>>((timesheetId, timesheetLines)));

        await harness.Sut.ValidateBeforePostAsync(
            AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice),
            CancellationToken.None);

        harness.LockedDocumentIds.Should().Equal(timesheetId);
    }

    private static SalesInvoiceHarness CreateSalesInvoiceHarness(
        AgencyBillingSalesInvoiceHead? head = null,
        IReadOnlyList<AgencyBillingSalesInvoiceLine>? lines = null,
        AgencyBillingTestData.ReferenceReadersStub? refs = null,
        IReadOnlyDictionary<Guid, DocumentRecord>? documentsById = null,
        IReadOnlyDictionary<Guid, AgencyBillingClientContractHead>? contractsById = null,
        IReadOnlyDictionary<Guid, AgencyBillingTimesheetHead>? timesheetHeadsById = null,
        IReadOnlyDictionary<Guid, IReadOnlyList<AgencyBillingTimesheetLine>>? timesheetLinesById = null,
        IReadOnlyDictionary<Guid, AgencyBillingTimesheetInvoiceUsage>? usageByTimesheetId = null)
    {
        head ??= AgencyBillingTestData.ValidSalesInvoiceHead(contractId: null);
        lines ??= [AgencyBillingTestData.ValidSalesInvoiceLine(documentId: head.DocumentId)];
        refs ??= ValidInvoiceReferences(head.ClientId, head.ProjectId, lines[0].ServiceItemId);

        var readers = new Mock<IAgencyBillingDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadSalesInvoiceHeadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(head);
        readers.Setup(x => x.ReadSalesInvoiceLinesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(lines);
        readers.Setup(x => x.ReadClientContractHeadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
            {
                if (contractsById is not null && contractsById.TryGetValue(id, out var contract))
                    return contract;

                return AgencyBillingTestData.ValidClientContractHead(
                    documentId: id,
                    clientId: head.ClientId,
                    projectId: head.ProjectId,
                    isActive: true);
            });
        readers.Setup(x => x.ReadTimesheetHeadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
            {
                if (timesheetHeadsById is not null && timesheetHeadsById.TryGetValue(id, out var timesheetHead))
                    return timesheetHead;

                return AgencyBillingTestData.ValidTimesheetHead(
                    documentId: id,
                    clientId: head.ClientId,
                    projectId: head.ProjectId);
            });
        readers.Setup(x => x.ReadTimesheetLinesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
            {
                if (timesheetLinesById is not null && timesheetLinesById.TryGetValue(id, out var timesheetLines))
                    return timesheetLines;

                return (IReadOnlyList<AgencyBillingTimesheetLine>)
                [
                    AgencyBillingTestData.ValidTimesheetLine(documentId: id)
                ];
            });

        var usageReader = new Mock<IAgencyBillingInvoiceUsageReader>(MockBehavior.Strict);
        usageReader.Setup(x => x.GetPostedInvoiceUsageForTimesheetAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid sourceTimesheetId, Guid? _, CancellationToken _) =>
            {
                if (usageByTimesheetId is not null && usageByTimesheetId.TryGetValue(sourceTimesheetId, out var usage))
                    return usage;

                return new AgencyBillingTimesheetInvoiceUsage(0m, 0m);
            });

        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        documents.Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                documentsById is not null && documentsById.TryGetValue(id, out var record) ? record : null);

        var locks = new Mock<IAdvisoryLockManager>(MockBehavior.Strict);
        var lockedDocumentIds = new List<Guid>();
        locks.Setup(x => x.LockDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((documentId, _) => lockedDocumentIds.Add(documentId))
            .Returns(Task.CompletedTask);

        return new SalesInvoiceHarness(
            new SalesInvoicePostValidator(readers.Object, usageReader.Object, refs, documents.Object, locks.Object),
            lockedDocumentIds);
    }

    private static AgencyBillingTestData.ReferenceReadersStub ValidInvoiceReferences(
        Guid? clientId = null,
        Guid? projectId = null,
        Guid? serviceItemId = null,
        Guid? projectClientId = null)
        => new()
        {
            ReadClientAsyncFunc = (_, _) => Task.FromResult<AgencyBillingClientReference?>(
                clientId is null ? null : AgencyBillingTestData.ClientReference(clientId)),
            ReadProjectAsyncFunc = (id, _) => Task.FromResult<AgencyBillingProjectReference?>(
                projectId == id
                    ? AgencyBillingTestData.ProjectReference(id, clientId: projectClientId ?? clientId)
                    : null),
            ReadServiceItemAsyncFunc = (id, _) => Task.FromResult<AgencyBillingServiceItemReference?>(
                id == serviceItemId ? AgencyBillingTestData.ServiceItemReference(id) : null)
        };

    private sealed record SalesInvoiceHarness(
        SalesInvoicePostValidator Sut,
        IReadOnlyList<Guid> LockedDocumentIds);

    private static Dictionary<Guid, TValue> Map<TValue>(params (Guid Key, TValue Value)[] items)
        => items.ToDictionary(static x => x.Key, static x => x.Value);
}

public sealed class CustomerPaymentPostValidator_P0Tests
{
    [Fact]
    public async Task ValidateBeforePostAsync_When_Bound_To_Different_Type_Throws()
    {
        var harness = CreateCustomerPaymentHarness();

        Func<Task> act = () => harness.Sut.ValidateBeforePostAsync(
            AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice),
            CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Client_Is_Invalid_Throws()
    {
        var payment = AgencyBillingTestData.ValidCustomerPaymentHead();
        var harness = CreateCustomerPaymentHarness(
            payment: payment,
            refs: ValidPaymentReferences());

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.CustomerPayment), CancellationToken.None));

        ex.ParamName.Should().Be("client_id");
        ex.Reason.Should().Be("Referenced client was not found.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task ValidateBeforePostAsync_When_Amount_Is_Not_Positive_Throws(decimal amount)
    {
        var payment = AgencyBillingTestData.ValidCustomerPaymentHead(amount: amount);
        var harness = CreateCustomerPaymentHarness(
            payment: payment,
            refs: ValidPaymentReferences(payment.ClientId));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.CustomerPayment), CancellationToken.None));

        ex.ParamName.Should().Be("amount");
        ex.Reason.Should().Be("Amount must be greater than zero.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Cash_Account_Is_Invalid_Throws()
    {
        var cashAccountId = Guid.NewGuid();
        var payment = AgencyBillingTestData.ValidCustomerPaymentHead(cashAccountId: cashAccountId);
        var harness = CreateCustomerPaymentHarness(
            payment: payment,
            refs: ValidPaymentReferences(payment.ClientId),
            chart: AgencyBillingTestData.CreateChart());

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.CustomerPayment), CancellationToken.None));

        ex.ParamName.Should().Be("cash_account_id");
        ex.Reason.Should().Be("Selected cash / bank account was not found.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_No_Applies_Exist_Throws()
    {
        var payment = AgencyBillingTestData.ValidCustomerPaymentHead();
        var harness = CreateCustomerPaymentHarness(
            payment: payment,
            applies: [],
            refs: ValidPaymentReferences(payment.ClientId));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.CustomerPayment), CancellationToken.None));

        ex.ParamName.Should().Be("applies");
        ex.Reason.Should().Be("Customer Payment must contain at least one apply row.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Ar_Open_Items_Register_Is_Missing_Throws()
    {
        var payment = AgencyBillingTestData.ValidCustomerPaymentHead();
        var apply = AgencyBillingTestData.ValidCustomerPaymentApply(documentId: payment.DocumentId);
        var policy = CreatePolicy(arOpenItemsRegisterId: Guid.NewGuid());
        var harness = CreateCustomerPaymentHarness(
            payment: payment,
            applies: [apply],
            refs: ValidPaymentReferences(payment.ClientId),
            policy: policy,
            register: null,
            registerExists: false);

        var ex = await Assert.ThrowsAsync<NgbConfigurationViolationException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.CustomerPayment), CancellationToken.None));

        ex.Message.Should().Contain(policy.ArOpenItemsOperationalRegisterId.ToString());
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Applied_Invoice_Is_Missing_Throws()
    {
        var payment = AgencyBillingTestData.ValidCustomerPaymentHead();
        var apply = AgencyBillingTestData.ValidCustomerPaymentApply(documentId: payment.DocumentId);
        var harness = CreateCustomerPaymentHarness(
            payment: payment,
            applies: [apply],
            refs: ValidPaymentReferences(payment.ClientId));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.CustomerPayment), CancellationToken.None));

        ex.ParamName.Should().Be("applies");
        ex.Reason.Should().Be("Referenced sales invoice was not found.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Applied_Invoice_Is_Not_Posted_Throws()
    {
        var payment = AgencyBillingTestData.ValidCustomerPaymentHead();
        var invoiceId = Guid.NewGuid();
        var apply = AgencyBillingTestData.ValidCustomerPaymentApply(documentId: payment.DocumentId, salesInvoiceId: invoiceId);
        var invoice = AgencyBillingTestData.ValidSalesInvoiceHead(documentId: invoiceId, clientId: payment.ClientId);
        var harness = CreateCustomerPaymentHarness(
            payment: payment,
            applies: [apply],
            refs: ValidPaymentReferences(payment.ClientId),
            documentsById: Map(
                (invoiceId, AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice, DocumentStatus.Draft, id: invoiceId))),
            invoicesById: Map((invoiceId, invoice)));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.CustomerPayment), CancellationToken.None));

        ex.ParamName.Should().Be("applies");
        ex.Reason.Should().Be("Referenced sales invoice must be posted.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Applied_Invoice_Client_Differs_Throws()
    {
        var payment = AgencyBillingTestData.ValidCustomerPaymentHead();
        var invoiceId = Guid.NewGuid();
        var apply = AgencyBillingTestData.ValidCustomerPaymentApply(documentId: payment.DocumentId, salesInvoiceId: invoiceId);
        var invoice = AgencyBillingTestData.ValidSalesInvoiceHead(
            documentId: invoiceId,
            clientId: Guid.NewGuid(),
            projectId: Guid.NewGuid(),
            amount: 500m);
        var harness = CreateCustomerPaymentHarness(
            payment: payment,
            applies: [apply],
            refs: ValidPaymentReferences(payment.ClientId),
            documentsById: Map(
                (invoiceId, AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice, DocumentStatus.Posted, id: invoiceId))),
            invoicesById: Map((invoiceId, invoice)),
            openAmountsByInvoiceId: Map((invoiceId, 500m)));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.CustomerPayment), CancellationToken.None));

        ex.ParamName.Should().Be("applies");
        ex.Reason.Should().Be("Payment client must match the client on every applied sales invoice.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-25)]
    public async Task ValidateBeforePostAsync_When_Applied_Amount_Is_Not_Positive_Throws(decimal appliedAmount)
    {
        var payment = AgencyBillingTestData.ValidCustomerPaymentHead(amount: 100m);
        var invoiceId = Guid.NewGuid();
        var apply = AgencyBillingTestData.ValidCustomerPaymentApply(
            documentId: payment.DocumentId,
            salesInvoiceId: invoiceId,
            appliedAmount: appliedAmount);
        var invoice = AgencyBillingTestData.ValidSalesInvoiceHead(
            documentId: invoiceId,
            clientId: payment.ClientId,
            projectId: Guid.NewGuid(),
            amount: 100m);
        var harness = CreateCustomerPaymentHarness(
            payment: payment,
            applies: [apply],
            refs: ValidPaymentReferences(payment.ClientId),
            documentsById: Map(
                (invoiceId, AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice, DocumentStatus.Posted, id: invoiceId))),
            invoicesById: Map((invoiceId, invoice)),
            openAmountsByInvoiceId: Map((invoiceId, 100m)));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.CustomerPayment), CancellationToken.None));

        ex.ParamName.Should().Be("applies");
        ex.Reason.Should().Be("Applied Amount must be greater than zero.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Applied_Amount_Exceeds_Open_Amount_Throws()
    {
        var payment = AgencyBillingTestData.ValidCustomerPaymentHead(amount: 450m);
        var invoiceId = Guid.NewGuid();
        var apply = AgencyBillingTestData.ValidCustomerPaymentApply(
            documentId: payment.DocumentId,
            salesInvoiceId: invoiceId,
            appliedAmount: 450m);
        var invoice = AgencyBillingTestData.ValidSalesInvoiceHead(
            documentId: invoiceId,
            clientId: payment.ClientId,
            projectId: Guid.NewGuid(),
            amount: 450m);
        var harness = CreateCustomerPaymentHarness(
            payment: payment,
            applies: [apply],
            refs: ValidPaymentReferences(payment.ClientId),
            documentsById: Map(
                (invoiceId, AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice, DocumentStatus.Posted, id: invoiceId))),
            invoicesById: Map((invoiceId, invoice)),
            openAmountsByInvoiceId: Map((invoiceId, 400m)));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.CustomerPayment), CancellationToken.None));

        ex.ParamName.Should().Be("applies");
        ex.Reason.Should().Contain("remaining open amount");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Payment_Amount_Does_Not_Equal_Applied_Total_Throws()
    {
        var payment = AgencyBillingTestData.ValidCustomerPaymentHead(amount: 500m);
        var invoiceId = Guid.NewGuid();
        var apply = AgencyBillingTestData.ValidCustomerPaymentApply(
            documentId: payment.DocumentId,
            salesInvoiceId: invoiceId,
            appliedAmount: 450m);
        var invoice = AgencyBillingTestData.ValidSalesInvoiceHead(
            documentId: invoiceId,
            clientId: payment.ClientId,
            projectId: Guid.NewGuid(),
            amount: 450m);
        var harness = CreateCustomerPaymentHarness(
            payment: payment,
            applies: [apply],
            refs: ValidPaymentReferences(payment.ClientId),
            documentsById: Map(
                (invoiceId, AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice, DocumentStatus.Posted, id: invoiceId))),
            invoicesById: Map((invoiceId, invoice)),
            openAmountsByInvoiceId: Map((invoiceId, 450m)));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            harness.Sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.CustomerPayment), CancellationToken.None));

        ex.ParamName.Should().Be("amount");
        ex.Reason.Should().Be("Payment Amount must equal the sum of Applied Amount values.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Payment_Is_Consistent_Passes_And_Locks_Each_Invoice_Once()
    {
        var cashAccountId = Guid.NewGuid();
        var payment = AgencyBillingTestData.ValidCustomerPaymentHead(cashAccountId: cashAccountId, amount: 500m);
        var invoiceId = Guid.NewGuid();
        var applies = new[]
        {
            AgencyBillingTestData.ValidCustomerPaymentApply(documentId: payment.DocumentId, ordinal: 1, salesInvoiceId: invoiceId, appliedAmount: 200m),
            AgencyBillingTestData.ValidCustomerPaymentApply(documentId: payment.DocumentId, ordinal: 2, salesInvoiceId: invoiceId, appliedAmount: 300m)
        };
        var invoice = AgencyBillingTestData.ValidSalesInvoiceHead(
            documentId: invoiceId,
            clientId: payment.ClientId,
            projectId: Guid.NewGuid(),
            amount: 500m);
        var chart = AgencyBillingTestData.CreateChart(
            AgencyBillingTestData.CreateAccount(id: cashAccountId, type: AccountType.Asset));
        var harness = CreateCustomerPaymentHarness(
            payment: payment,
            applies: applies,
            refs: ValidPaymentReferences(payment.ClientId),
            chart: chart,
            documentsById: Map(
                (invoiceId, AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice, DocumentStatus.Posted, id: invoiceId))),
            invoicesById: Map((invoiceId, invoice)),
            openAmountsByInvoiceId: Map((invoiceId, 500m)));

        await harness.Sut.ValidateBeforePostAsync(
            AgencyBillingTestData.CreateDocument(AgencyBillingCodes.CustomerPayment),
            CancellationToken.None);

        harness.LockedDocumentIds.Should().Equal(invoiceId);
    }

    private static CustomerPaymentHarness CreateCustomerPaymentHarness(
        AgencyBillingCustomerPaymentHead? payment = null,
        IReadOnlyList<AgencyBillingCustomerPaymentApply>? applies = null,
        AgencyBillingTestData.ReferenceReadersStub? refs = null,
        IReadOnlyDictionary<Guid, DocumentRecord>? documentsById = null,
        IReadOnlyDictionary<Guid, AgencyBillingSalesInvoiceHead>? invoicesById = null,
        IReadOnlyDictionary<Guid, decimal>? openAmountsByInvoiceId = null,
        ChartOfAccounts? chart = null,
        AgencyBillingAccountingPolicy? policy = null,
        OperationalRegisterAdminItem? register = null,
        bool registerExists = true)
    {
        payment ??= AgencyBillingTestData.ValidCustomerPaymentHead();
        applies ??= [AgencyBillingTestData.ValidCustomerPaymentApply(documentId: payment.DocumentId)];
        refs ??= ValidPaymentReferences(payment.ClientId);
        policy ??= CreatePolicy();
        if (registerExists && register is null)
            register = AgencyBillingTestData.Register(registerId: policy.ArOpenItemsOperationalRegisterId, code: AgencyBillingCodes.ArOpenItemsRegisterCode);

        var readers = new Mock<IAgencyBillingDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadCustomerPaymentHeadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        readers.Setup(x => x.ReadCustomerPaymentAppliesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(applies);
        readers.Setup(x => x.ReadSalesInvoiceHeadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
            {
                if (invoicesById is not null && invoicesById.TryGetValue(id, out var invoice))
                    return invoice;

                return AgencyBillingTestData.ValidSalesInvoiceHead(documentId: id, clientId: payment.ClientId, amount: 0m);
            });

        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        documents.Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                documentsById is not null && documentsById.TryGetValue(id, out var document) ? document : null);

        var charts = new Mock<IChartOfAccountsProvider>(MockBehavior.Strict);
        charts.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(chart ?? AgencyBillingTestData.CreateChart());

        var policyReader = new Mock<IAgencyBillingAccountingPolicyReader>(MockBehavior.Strict);
        policyReader.Setup(x => x.GetRequiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        var registers = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        registers.Setup(x => x.GetByIdAsync(policy.ArOpenItemsOperationalRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(register);

        var netReader = new Mock<IOperationalRegisterResourceNetReader>(MockBehavior.Strict);
        netReader.Setup(x => x.GetNetByDimensionSetAsync(
                policy.ArOpenItemsOperationalRegisterId,
                It.IsAny<Guid>(),
                "amount",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid dimensionSetId, string _, CancellationToken _) =>
            {
                if (openAmountsByInvoiceId is null || invoicesById is null)
                    return 0m;

                foreach (var (invoiceId, invoice) in invoicesById)
                {
                    var expectedDimensionSetId = DeterministicDimensionSetId.FromBag(
                        AgencyBillingPostingCommon.ArOpenItemBag(invoice.ClientId, invoice.ProjectId, invoice.DocumentId));
                    if (expectedDimensionSetId == dimensionSetId
                        && openAmountsByInvoiceId.TryGetValue(invoiceId, out var openAmount))
                    {
                        return openAmount;
                    }
                }

                return 0m;
            });

        var locks = new Mock<IAdvisoryLockManager>(MockBehavior.Strict);
        var lockedDocumentIds = new List<Guid>();
        locks.Setup(x => x.LockDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((documentId, _) => lockedDocumentIds.Add(documentId))
            .Returns(Task.CompletedTask);

        return new CustomerPaymentHarness(
            new CustomerPaymentPostValidator(
                readers.Object,
                refs,
                documents.Object,
                charts.Object,
                policyReader.Object,
                registers.Object,
                netReader.Object,
                locks.Object),
            lockedDocumentIds);
    }

    private static AgencyBillingAccountingPolicy CreatePolicy(Guid? arOpenItemsRegisterId = null)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            arOpenItemsRegisterId ?? Guid.NewGuid());

    private static AgencyBillingTestData.ReferenceReadersStub ValidPaymentReferences(Guid? clientId = null)
        => new()
        {
            ReadClientAsyncFunc = (_, _) => Task.FromResult<AgencyBillingClientReference?>(
                clientId is null ? null : AgencyBillingTestData.ClientReference(clientId))
        };

    private sealed record CustomerPaymentHarness(
        CustomerPaymentPostValidator Sut,
        IReadOnlyList<Guid> LockedDocumentIds);

    private static Dictionary<Guid, TValue> Map<TValue>(params (Guid Key, TValue Value)[] items)
        => items.ToDictionary(static x => x.Key, static x => x.Value);
}
