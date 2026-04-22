using FluentAssertions;
using NGB.AgencyBilling.Documents;
using NGB.AgencyBilling.Enums;
using NGB.AgencyBilling.References;
using NGB.AgencyBilling.Runtime.Documents.Validation;
using NGB.AgencyBilling.Runtime.Tests.Infrastructure;
using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Tests.Documents;

public sealed class ClientContractPostValidator_P0Tests
{
    [Fact]
    public async Task ValidateBeforePostAsync_When_Bound_To_Different_Type_Throws()
    {
        var sut = CreateSut();

        Func<Task> act = () => sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet), CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Payment_Terms_Are_Invalid_Throws()
    {
        var head = AgencyBillingTestData.ValidClientContractHead(paymentTermsId: Guid.NewGuid());
        var refs = ValidReferences(head.ClientId, head.ProjectId);
        var sut = CreateSut(head: head, refs: refs);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.ClientContract), CancellationToken.None));

        ex.ParamName.Should().Be("payment_terms_id");
        ex.Reason.Should().Be("Referenced payment terms was not found.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Contract_Is_Not_Active_Throws()
    {
        var head = AgencyBillingTestData.ValidClientContractHead(isActive: false);
        var sut = CreateSut(head: head, refs: ValidReferences(head.ClientId, head.ProjectId));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.ClientContract), CancellationToken.None));

        ex.ParamName.Should().Be("is_active");
        ex.Reason.Should().Be("Client Contract must be active before posting.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Effective_To_Is_Before_Effective_From_Throws()
    {
        var head = AgencyBillingTestData.ValidClientContractHead(effectiveTo: new DateOnly(2026, 3, 31));
        var sut = CreateSut(head: head, refs: ValidReferences(head.ClientId, head.ProjectId));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.ClientContract), CancellationToken.None));

        ex.ParamName.Should().Be("effective_to");
        ex.Reason.Should().Be("Effective To must be on or after Effective From.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_No_Lines_Exist_Throws()
    {
        var head = AgencyBillingTestData.ValidClientContractHead();
        var sut = CreateSut(head: head, lines: [], refs: ValidReferences(head.ClientId, head.ProjectId));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.ClientContract), CancellationToken.None));

        ex.ParamName.Should().Be("lines");
        ex.Reason.Should().Be("Client Contract must contain at least one line.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Line_Service_Item_Is_Invalid_Throws()
    {
        var head = AgencyBillingTestData.ValidClientContractHead();
        var line = AgencyBillingTestData.ValidClientContractLine();
        var sut = CreateSut(head: head, lines: [line], refs: ValidReferences(head.ClientId, head.ProjectId));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.ClientContract), CancellationToken.None));

        ex.ParamName.Should().Be("lines[0].service_item_id");
        ex.Reason.Should().Be("Referenced service item was not found.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Line_Team_Member_Is_Invalid_Throws()
    {
        var head = AgencyBillingTestData.ValidClientContractHead();
        var line = AgencyBillingTestData.ValidClientContractLine();
        var refs = ValidReferences(head.ClientId, head.ProjectId, serviceItemId: line.ServiceItemId!.Value);
        var sut = CreateSut(head: head, lines: [line], refs: refs);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.ClientContract), CancellationToken.None));

        ex.ParamName.Should().Be("lines[0].team_member_id");
        ex.Reason.Should().Be("Referenced team member was not found.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateBeforePostAsync_When_Line_Billing_Rate_Is_Not_Positive_Throws(decimal billingRate)
    {
        var head = AgencyBillingTestData.ValidClientContractHead();
        var line = AgencyBillingTestData.ValidClientContractLine(billingRate: billingRate);
        var refs = ValidReferences(head.ClientId, head.ProjectId, line.ServiceItemId!.Value, line.TeamMemberId!.Value);
        var sut = CreateSut(head: head, lines: [line], refs: refs);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.ClientContract), CancellationToken.None));

        ex.ParamName.Should().Be("lines[0].billing_rate");
        ex.Reason.Should().Be("Billing Rate must be greater than zero.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Line_Cost_Rate_Is_Negative_Throws()
    {
        var head = AgencyBillingTestData.ValidClientContractHead();
        var line = AgencyBillingTestData.ValidClientContractLine(costRate: -1m);
        var refs = ValidReferences(head.ClientId, head.ProjectId, line.ServiceItemId!.Value, line.TeamMemberId!.Value);
        var sut = CreateSut(head: head, lines: [line], refs: refs);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.ClientContract), CancellationToken.None));

        ex.ParamName.Should().Be("lines[0].cost_rate");
        ex.Reason.Should().Be("Cost Rate must be zero or greater.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Line_Active_To_Is_Before_Active_From_Throws()
    {
        var head = AgencyBillingTestData.ValidClientContractHead();
        var line = AgencyBillingTestData.ValidClientContractLine() with
        {
            ActiveFrom = new DateOnly(2026, 4, 18),
            ActiveTo = new DateOnly(2026, 4, 17),
        };
        var refs = ValidReferences(head.ClientId, head.ProjectId, line.ServiceItemId!.Value, line.TeamMemberId!.Value);
        var sut = CreateSut(head: head, lines: [line], refs: refs);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.ClientContract), CancellationToken.None));

        ex.ParamName.Should().Be("lines[0].active_to");
        ex.Reason.Should().Be("Active To must be on or after Active From.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Line_Has_No_Service_Discriminator_Throws()
    {
        var head = AgencyBillingTestData.ValidClientContractHead();
        var line = AgencyBillingTestData.ValidClientContractLine() with
        {
            ServiceItemId = null,
            TeamMemberId = null,
            ServiceTitle = "   ",
        };
        var sut = CreateSut(head: head, lines: [line], refs: ValidReferences(head.ClientId, head.ProjectId));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.ClientContract), CancellationToken.None));

        ex.ParamName.Should().Be("lines[0]");
        ex.Reason.Should().Be("Contract line must specify at least a Service Item, Team Member, or Service Title.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Contract_Is_Consistent_Passes()
    {
        var head = AgencyBillingTestData.ValidClientContractHead(paymentTermsId: Guid.NewGuid());
        var firstLine = AgencyBillingTestData.ValidClientContractLine(documentId: head.DocumentId);
        var secondLine = AgencyBillingTestData.ValidClientContractLine(
            documentId: head.DocumentId,
            ordinal: 2,
            serviceItemId: Guid.NewGuid(),
            teamMemberId: Guid.NewGuid(),
            billingRate: 120m,
            costRate: 50m,
            serviceTitle: "Optimization");
        var refs = ValidReferences(
            head.ClientId,
            head.ProjectId,
            paymentTermsId: head.PaymentTermsId!.Value,
            serviceItemId: firstLine.ServiceItemId!.Value,
            teamMemberId: firstLine.TeamMemberId!.Value,
            extraServiceItemId: secondLine.ServiceItemId!.Value,
            extraTeamMemberId: secondLine.TeamMemberId!.Value);
        var sut = CreateSut(head: head, lines: [firstLine, secondLine], refs: refs);

        await sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.ClientContract), CancellationToken.None);
    }

    private static ClientContractPostValidator CreateSut(
        AgencyBillingClientContractHead? head = null,
        IReadOnlyList<AgencyBillingClientContractLine>? lines = null,
        AgencyBillingTestData.ReferenceReadersStub? refs = null)
        => new(
            new AgencyBillingTestData.DocumentReadersStub
            {
                ClientContractHead = head ?? AgencyBillingTestData.ValidClientContractHead(),
                ClientContractLines = lines ?? [AgencyBillingTestData.ValidClientContractLine()],
            },
            refs ?? ValidReferences(Guid.NewGuid(), Guid.NewGuid()));

    private static AgencyBillingTestData.ReferenceReadersStub ValidReferences(
        Guid clientId,
        Guid projectId,
        Guid? serviceItemId = null,
        Guid? teamMemberId = null,
        Guid? paymentTermsId = null,
        Guid? extraServiceItemId = null,
        Guid? extraTeamMemberId = null)
        => new()
        {
            ReadClientAsyncFunc = (_, _) => Task.FromResult<AgencyBillingClientReference?>(
                AgencyBillingTestData.ClientReference(clientId)),
            ReadProjectAsyncFunc = (_, _) => Task.FromResult<AgencyBillingProjectReference?>(
                AgencyBillingTestData.ProjectReference(projectId, clientId: clientId)),
            ReadPaymentTermsAsyncFunc = (id, _) => Task.FromResult<AgencyBillingPaymentTermsReference?>(
                id == paymentTermsId ? AgencyBillingTestData.PaymentTermsReference(id) : null),
            ReadServiceItemAsyncFunc = (id, _) => Task.FromResult<AgencyBillingServiceItemReference?>(
                id == serviceItemId || id == extraServiceItemId ? AgencyBillingTestData.ServiceItemReference(id) : null),
            ReadTeamMemberAsyncFunc = (id, _) => Task.FromResult<AgencyBillingTeamMemberReference?>(
                id == teamMemberId || id == extraTeamMemberId ? AgencyBillingTestData.TeamMemberReference(id) : null),
        };
}

public sealed class TimesheetPostValidator_P0Tests
{
    [Fact]
    public async Task ValidateBeforePostAsync_When_Bound_To_Different_Type_Throws()
    {
        var sut = CreateSut();

        Func<Task> act = () => sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice), CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Lines_Are_Missing_Throws()
    {
        var head = AgencyBillingTestData.ValidTimesheetHead();
        var sut = CreateSut(head: head, lines: [], refs: ValidReferences(head.ClientId, head.ProjectId, head.TeamMemberId));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet), CancellationToken.None));

        ex.ParamName.Should().Be("lines");
        ex.Reason.Should().Be("Timesheet must contain at least one line.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Service_Item_Is_Invalid_Throws()
    {
        var head = AgencyBillingTestData.ValidTimesheetHead();
        var line = AgencyBillingTestData.ValidTimesheetLine(documentId: head.DocumentId);
        var sut = CreateSut(head: head, lines: [line], refs: ValidReferences(head.ClientId, head.ProjectId, head.TeamMemberId));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet), CancellationToken.None));

        ex.ParamName.Should().Be("lines[0].service_item_id");
        ex.Reason.Should().Be("Referenced service item was not found.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateBeforePostAsync_When_Hours_Are_Not_Positive_Throws(decimal hours)
    {
        var head = AgencyBillingTestData.ValidTimesheetHead(totalHours: hours);
        var line = AgencyBillingTestData.ValidTimesheetLine(documentId: head.DocumentId, hours: hours, lineAmount: 0m, billable: false);
        var refs = ValidReferences(head.ClientId, head.ProjectId, head.TeamMemberId, line.ServiceItemId!.Value);
        var sut = CreateSut(head: head, lines: [line], refs: refs);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet), CancellationToken.None));

        ex.ParamName.Should().Be("lines[0].hours");
        ex.Reason.Should().Be("Hours must be greater than zero.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("-1")]
    public async Task ValidateBeforePostAsync_When_Cost_Rate_Is_Missing_Or_Negative_Throws(string? costRateRaw)
    {
        var head = AgencyBillingTestData.ValidTimesheetHead();
        var costRate = costRateRaw is null ? (decimal?)null : decimal.Parse(costRateRaw, System.Globalization.CultureInfo.InvariantCulture);
        var line = AgencyBillingTestData.ValidTimesheetLine(documentId: head.DocumentId, costRate: costRate);
        var refs = ValidReferences(head.ClientId, head.ProjectId, head.TeamMemberId, line.ServiceItemId!.Value);
        var sut = CreateSut(head: head, lines: [line], refs: refs);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet), CancellationToken.None));

        ex.ParamName.Should().Be("lines[0].cost_rate");
        ex.Reason.Should().Be("Cost Rate Snapshot is required and must be zero or greater.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("-1")]
    public async Task ValidateBeforePostAsync_When_Line_Cost_Amount_Is_Missing_Or_Negative_Throws(string? lineCostAmountRaw)
    {
        var head = AgencyBillingTestData.ValidTimesheetHead();
        var lineCostAmount = lineCostAmountRaw is null ? (decimal?)null : decimal.Parse(lineCostAmountRaw, System.Globalization.CultureInfo.InvariantCulture);
        var line = AgencyBillingTestData.ValidTimesheetLine(documentId: head.DocumentId, lineCostAmount: lineCostAmount);
        var refs = ValidReferences(head.ClientId, head.ProjectId, head.TeamMemberId, line.ServiceItemId!.Value);
        var sut = CreateSut(head: head, lines: [line], refs: refs);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet), CancellationToken.None));

        ex.ParamName.Should().Be("lines[0].line_cost_amount");
        ex.Reason.Should().Be("Line Cost Amount is required and must be zero or greater.");
    }

    [Theory]
    [InlineData(8, 65, 519.9, "520")]
    [InlineData(2.3456, 1.1111, 2.6061, "2.6062")]
    public async Task ValidateBeforePostAsync_When_Line_Cost_Amount_Does_Not_Match_Hours_Times_CostRate_Throws(
        decimal hours,
        decimal costRate,
        decimal lineCostAmount,
        string expectedDisplay)
    {
        var head = AgencyBillingTestData.ValidTimesheetHead(totalHours: hours, costAmount: lineCostAmount);
        var line = AgencyBillingTestData.ValidTimesheetLine(documentId: head.DocumentId, hours: hours, costRate: costRate, lineCostAmount: lineCostAmount);
        var refs = ValidReferences(head.ClientId, head.ProjectId, head.TeamMemberId, line.ServiceItemId!.Value);
        var sut = CreateSut(head: head, lines: [line], refs: refs);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet), CancellationToken.None));

        ex.ParamName.Should().Be("lines[0].line_cost_amount");
        ex.Reason.Should().Be($"Line Cost Amount must equal Hours x Cost Rate ({expectedDisplay}).");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    public async Task ValidateBeforePostAsync_When_Billable_Line_Billing_Rate_Is_Invalid_Throws(string? billingRateRaw)
    {
        var head = AgencyBillingTestData.ValidTimesheetHead();
        var billingRate = billingRateRaw is null ? (decimal?)null : decimal.Parse(billingRateRaw, System.Globalization.CultureInfo.InvariantCulture);
        var line = AgencyBillingTestData.ValidTimesheetLine(documentId: head.DocumentId, billingRate: billingRate);
        var refs = ValidReferences(head.ClientId, head.ProjectId, head.TeamMemberId, line.ServiceItemId!.Value);
        var sut = CreateSut(head: head, lines: [line], refs: refs);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet), CancellationToken.None));

        ex.ParamName.Should().Be("lines[0].billing_rate");
        ex.Reason.Should().Be("Billing Rate Snapshot is required and must be greater than zero for billable time.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    public async Task ValidateBeforePostAsync_When_Billable_Line_Amount_Is_Invalid_Throws(string? lineAmountRaw)
    {
        var head = AgencyBillingTestData.ValidTimesheetHead();
        var lineAmount = lineAmountRaw is null ? (decimal?)null : decimal.Parse(lineAmountRaw, System.Globalization.CultureInfo.InvariantCulture);
        var line = AgencyBillingTestData.ValidTimesheetLine(documentId: head.DocumentId, lineAmount: lineAmount);
        var refs = ValidReferences(head.ClientId, head.ProjectId, head.TeamMemberId, line.ServiceItemId!.Value);
        var sut = CreateSut(head: head, lines: [line], refs: refs);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet), CancellationToken.None));

        ex.ParamName.Should().Be("lines[0].line_amount");
        ex.Reason.Should().Be("Line Amount is required and must be greater than zero for billable time.");
    }

    [Theory]
    [InlineData(8, 160, 1279.9, "1280")]
    [InlineData(2.3456, 1.1111, 2.6061, "2.6062")]
    public async Task ValidateBeforePostAsync_When_Billable_Line_Amount_Does_Not_Match_Hours_Times_Rate_Throws(
        decimal hours,
        decimal billingRate,
        decimal lineAmount,
        string expectedDisplay)
    {
        var head = AgencyBillingTestData.ValidTimesheetHead(totalHours: hours, amount: lineAmount);
        var validCostAmount = NGB.AgencyBilling.Runtime.Posting.AgencyBillingPostingCommon.RoundScale4(hours * 65m);
        var line = AgencyBillingTestData.ValidTimesheetLine(
            documentId: head.DocumentId,
            hours: hours,
            billingRate: billingRate,
            lineAmount: lineAmount,
            costRate: 65m,
            lineCostAmount: validCostAmount);
        var refs = ValidReferences(head.ClientId, head.ProjectId, head.TeamMemberId, line.ServiceItemId!.Value);
        var sut = CreateSut(head: head, lines: [line], refs: refs);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet), CancellationToken.None));

        ex.ParamName.Should().Be("lines[0].line_amount");
        ex.Reason.Should().Be($"Line Amount must equal Hours x Billing Rate ({expectedDisplay}).");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_NonBillable_Line_Has_NonZero_Amount_Throws()
    {
        var head = AgencyBillingTestData.ValidTimesheetHead(amount: 0m);
        var line = AgencyBillingTestData.ValidTimesheetLine(documentId: head.DocumentId, billable: false, lineAmount: 1m, billingRate: null);
        var refs = ValidReferences(head.ClientId, head.ProjectId, head.TeamMemberId, line.ServiceItemId!.Value);
        var sut = CreateSut(head: head, lines: [line], refs: refs);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet), CancellationToken.None));

        ex.ParamName.Should().Be("lines[0].line_amount");
        ex.Reason.Should().Be("Non-billable time must not carry billable amount.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Total_Hours_Do_Not_Match_Line_Sum_Throws()
    {
        var head = AgencyBillingTestData.ValidTimesheetHead(totalHours: 9m);
        var line = AgencyBillingTestData.ValidTimesheetLine(documentId: head.DocumentId);
        var refs = ValidReferences(head.ClientId, head.ProjectId, head.TeamMemberId, line.ServiceItemId!.Value);
        var sut = CreateSut(head: head, lines: [line], refs: refs);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet), CancellationToken.None));

        ex.ParamName.Should().Be("total_hours");
        ex.Reason.Should().Be("Total Hours must equal the sum of line hours.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Amount_Does_Not_Match_Billable_Line_Sum_Throws()
    {
        var head = AgencyBillingTestData.ValidTimesheetHead(amount: 1000m);
        var line = AgencyBillingTestData.ValidTimesheetLine(documentId: head.DocumentId);
        var refs = ValidReferences(head.ClientId, head.ProjectId, head.TeamMemberId, line.ServiceItemId!.Value);
        var sut = CreateSut(head: head, lines: [line], refs: refs);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet), CancellationToken.None));

        ex.ParamName.Should().Be("amount");
        ex.Reason.Should().Be("Amount must equal the sum of billable line amounts.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Cost_Amount_Does_Not_Match_Line_Cost_Sum_Throws()
    {
        var head = AgencyBillingTestData.ValidTimesheetHead(costAmount: 100m);
        var line = AgencyBillingTestData.ValidTimesheetLine(documentId: head.DocumentId);
        var refs = ValidReferences(head.ClientId, head.ProjectId, head.TeamMemberId, line.ServiceItemId!.Value);
        var sut = CreateSut(head: head, lines: [line], refs: refs);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet), CancellationToken.None));

        ex.ParamName.Should().Be("cost_amount");
        ex.Reason.Should().Be("Cost Amount must equal the sum of line cost amounts.");
    }

    [Fact]
    public async Task ValidateBeforePostAsync_When_Timesheet_Is_Consistent_Passes()
    {
        var head = AgencyBillingTestData.ValidTimesheetHead(totalHours: 10m, amount: 1280m, costAmount: 610m);
        var firstLine = AgencyBillingTestData.ValidTimesheetLine(documentId: head.DocumentId);
        var secondLine = AgencyBillingTestData.ValidTimesheetLine(
            documentId: head.DocumentId,
            ordinal: 2,
            serviceItemId: Guid.NewGuid(),
            hours: 2m,
            billable: false,
            billingRate: null,
            costRate: 45m,
            lineAmount: 0m,
            lineCostAmount: 90m,
            description: "Internal QA");
        var refs = ValidReferences(
            head.ClientId,
            head.ProjectId,
            head.TeamMemberId,
            firstLine.ServiceItemId!.Value,
            secondLine.ServiceItemId!.Value);
        var sut = CreateSut(head: head, lines: [firstLine, secondLine], refs: refs);

        await sut.ValidateBeforePostAsync(AgencyBillingTestData.CreateDocument(AgencyBillingCodes.Timesheet), CancellationToken.None);
    }

    private static TimesheetPostValidator CreateSut(
        AgencyBillingTimesheetHead? head = null,
        IReadOnlyList<AgencyBillingTimesheetLine>? lines = null,
        AgencyBillingTestData.ReferenceReadersStub? refs = null)
        => new(
            new AgencyBillingTestData.DocumentReadersStub
            {
                TimesheetHead = head ?? AgencyBillingTestData.ValidTimesheetHead(),
                TimesheetLines = lines ?? [AgencyBillingTestData.ValidTimesheetLine()],
            },
            refs ?? ValidReferences(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

    private static AgencyBillingTestData.ReferenceReadersStub ValidReferences(
        Guid clientId,
        Guid projectId,
        Guid teamMemberId,
        Guid? serviceItemId = null,
        Guid? extraServiceItemId = null)
        => new()
        {
            ReadClientAsyncFunc = (_, _) => Task.FromResult<AgencyBillingClientReference?>(
                AgencyBillingTestData.ClientReference(clientId, status: AgencyBillingClientStatus.Active)),
            ReadProjectAsyncFunc = (_, _) => Task.FromResult<AgencyBillingProjectReference?>(
                AgencyBillingTestData.ProjectReference(projectId, clientId: clientId, status: AgencyBillingProjectStatus.Active)),
            ReadTeamMemberAsyncFunc = (_, _) => Task.FromResult<AgencyBillingTeamMemberReference?>(
                AgencyBillingTestData.TeamMemberReference(teamMemberId)),
            ReadServiceItemAsyncFunc = (id, _) => Task.FromResult<AgencyBillingServiceItemReference?>(
                id == serviceItemId || id == extraServiceItemId ? AgencyBillingTestData.ServiceItemReference(id) : null),
        };
}
