using FluentAssertions;
using Moq;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.CRM.Documents;
using NGB.CRM.Runtime.Posting;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Dimensions;

namespace NGB.CRM.Runtime.Tests.Posting;

public sealed class CrmReferenceRegisterPostingHandlersFullCoverageTests
{
    private readonly Guid _documentId = Guid.CreateVersion7();
    private readonly Guid _dimensionSetId = Guid.CreateVersion7();

    [Fact]
    public async Task LeadIntake_EmitsLiveAndDeletedFunnelRecords()
    {
        var readers = Readers();
        var handler = new LeadIntakeReferenceRegisterPostingHandler(readers, Dimensions());

        handler.TypeCode.Should().Be(CrmCodes.LeadIntake);
        var records = await BuildBothAsync(handler);

        records.Should().HaveCount(2);
        records.Select(item => item.Record.IsDeleted).Should().Equal(false, true);
        records[0].Code.Should().Be(CrmCodes.LeadFunnelRegisterCode);
        records[0].Record.Values["funnel_step"].Should().Be("01 Intake");
    }

    [Theory]
    [InlineData("Qualified", "02 Qualified")]
    [InlineData("Disqualified", "02 Disqualified")]
    [InlineData("Converted", "03 Converted")]
    [InlineData("Review", "02 Review")]
    public async Task Qualification_MapsEveryFunnelState(string state, string expected)
    {
        var readers = Readers(qualificationState: state);
        var handler = new LeadQualificationReferenceRegisterPostingHandler(readers, Dimensions());
        var records = new List<CapturedRecord>();

        handler.TypeCode.Should().Be(CrmCodes.LeadQualification);
        await handler.BuildRecordsAsync(Document(), ReferenceRegisterWriteOperation.Post, Builder(records), CancellationToken.None);
        if (state == "Qualified")
            await handler.BuildRecordsAsync(Document(), ReferenceRegisterWriteOperation.Unpost, Builder(records), CancellationToken.None);

        records[0].Record.Values["funnel_step"].Should().Be(expected);
        if (state == "Qualified") records[1].Record.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Conversion_WithoutOpportunity_OnlyEmitsFunnelRecord()
    {
        var handler = new LeadConversionReferenceRegisterPostingHandler(
            Readers(createOpportunity: false), Dimensions());
        var records = await BuildBothAsync(handler);

        handler.TypeCode.Should().Be(CrmCodes.LeadConversion);
        records.Should().HaveCount(2);
        records.Should().OnlyContain(item => item.Code == CrmCodes.LeadFunnelRegisterCode);
    }

    [Theory]
    [InlineData(null, null, null, "Opportunity", "USD", 0, 0)]
    [InlineData("  Named  ", " eur ", "ignored", "Named", "EUR", 15, 75)]
    public async Task Conversion_WithOpportunity_CoversFallbacksAndAddsTwoRecordsPerOperation(
        string? name, string? currency, string? marker, string expectedName, string expectedCurrency,
        int amount, int probability)
    {
        _ = marker;
        var handler = new LeadConversionReferenceRegisterPostingHandler(
            Readers(createOpportunity: true, opportunityName: name, currency: currency,
                amount: amount, probability: probability), Dimensions());
        var records = await BuildBothAsync(handler);

        records.Should().HaveCount(4);
        var opportunities = records.Where(item => item.Code == CrmCodes.OpportunitiesRegisterCode).ToArray();
        opportunities.Should().HaveCount(2);
        opportunities[0].Record.Values["opportunity_name"].Should().Be(expectedName);
        opportunities[0].Record.Values["currency"].Should().Be(expectedCurrency);
        opportunities.Select(item => item.Record.IsDeleted).Should().Equal(false, true);
    }

    [Theory]
    [InlineData(null, "Opportunity", "USD")]
    [InlineData("  Existing  ", "Existing", "CAD")]
    public async Task OpportunityUpdate_CoversConversionFallbacksAndDeleteFlag(
        string? conversionName, string expectedName, string currency)
    {
        var handler = new OpportunityUpdateReferenceRegisterPostingHandler(
            Readers(opportunityName: conversionName, currency: currency), Dimensions());
        var records = await BuildBothAsync(handler);

        handler.TypeCode.Should().Be(CrmCodes.OpportunityUpdate);
        records.Should().HaveCount(2);
        records[0].Record.Values["opportunity_name"].Should().Be(expectedName);
        records[0].Record.Values["currency"].Should().Be(currency.ToUpperInvariant());
        records.Select(item => item.Record.IsDeleted).Should().Equal(false, true);
    }

    [Fact]
    public async Task Quote_EmitsLiveAndDeletedRecords()
    {
        var handler = new QuoteReferenceRegisterPostingHandler(Readers(currency: " gbp "), Dimensions());
        var records = await BuildBothAsync(handler);

        handler.TypeCode.Should().Be(CrmCodes.Quote);
        records.Should().HaveCount(2);
        records[0].Record.Values["currency"].Should().Be("GBP");
        records.Select(item => item.Record.IsDeleted).Should().Equal(false, true);
    }

    [Fact]
    public async Task Activity_EmitsLiveAndDeletedRecords()
    {
        var handler = new ActivityLogReferenceRegisterPostingHandler(Readers(), Dimensions());
        var records = await BuildBothAsync(handler);

        handler.TypeCode.Should().Be(CrmCodes.ActivityLog);
        records.Should().HaveCount(2);
        records[0].Record.Values["subject"].Should().Be("Call customer");
        records.Select(item => item.Record.IsDeleted).Should().Equal(false, true);
    }

    [Fact]
    public async Task UnknownTypeCode_IsAnExplicitNoOp()
    {
        var records = new List<CapturedRecord>();
        var handler = new UnknownHandler(Readers(), Dimensions());

        handler.TypeCode.Should().Be("crm.unknown");
        await handler.BuildRecordsAsync(Document(), ReferenceRegisterWriteOperation.Repost, Builder(records), CancellationToken.None);

        records.Should().BeEmpty();
    }

    private async Task<List<CapturedRecord>> BuildBothAsync(CrmReferenceRegisterPostingHandler handler)
    {
        var records = new List<CapturedRecord>();
        await handler.BuildRecordsAsync(Document(), ReferenceRegisterWriteOperation.Post, Builder(records), CancellationToken.None);
        await handler.BuildRecordsAsync(Document(), ReferenceRegisterWriteOperation.Unpost, Builder(records), CancellationToken.None);
        return records;
    }

    private ICrmDocumentReaders Readers(
        string qualificationState = "Qualified",
        bool createOpportunity = true,
        string? opportunityName = "Opportunity",
        string? currency = "USD",
        decimal? amount = 10m,
        decimal? probability = 50m)
    {
        var leadId = Guid.CreateVersion7();
        var opportunityId = Guid.CreateVersion7();
        var lead = new CrmLeadIntakeHead(leadId, new DateOnly(2026, 8, 1), "Lead", "Company", "Contact",
            "a@example.com", null, "Web", "Tech", 100m, currency, null);
        var qualification = new CrmLeadQualificationHead(_documentId, new DateOnly(2026, 8, 2), leadId,
            qualificationState, 80, null, null);
        var conversion = new CrmLeadConversionHead(opportunityId, new DateOnly(2026, 8, 3), leadId,
            Guid.CreateVersion7(), Guid.CreateVersion7(), createOpportunity, opportunityName, Guid.CreateVersion7(),
            amount, probability, new DateOnly(2026, 9, 1), currency, null);
        var update = new CrmOpportunityUpdateHead(_documentId, new DateOnly(2026, 8, 4), opportunityId,
            Guid.CreateVersion7(), 20m, 60m, new DateOnly(2026, 9, 2), "Open", null, null);
        var quote = new CrmQuoteHead(_documentId, new DateOnly(2026, 8, 5), opportunityId,
            Guid.CreateVersion7(), Guid.CreateVersion7(), new DateOnly(2026, 8, 20), currency ?? "",
            "Draft", 20m, null);
        var activity = new CrmActivityLogHead(_documentId, new DateOnly(2026, 8, 6), "Call", "Call customer",
            leadId, null, null, opportunityId, null, null, null, null);
        return new ReaderStub(lead, qualification, conversion, update, quote, activity);
    }

    private IDimensionSetService Dimensions()
    {
        var service = new Mock<IDimensionSetService>(MockBehavior.Strict);
        service.Setup(x => x.GetOrCreateIdAsync(It.IsAny<DimensionBag>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_dimensionSetId);
        return service.Object;
    }

    private static IReferenceRegisterRecordsBuilder Builder(ICollection<CapturedRecord> records)
    {
        var builder = new Mock<IReferenceRegisterRecordsBuilder>(MockBehavior.Strict);
        builder.Setup(x => x.Add(It.IsAny<string>(), It.IsAny<ReferenceRegisterRecordWrite>()))
            .Callback<string, ReferenceRegisterRecordWrite>((code, record) => records.Add(new(code, record)));
        return builder.Object;
    }

    private DocumentRecord Document() => new()
    {
        Id = _documentId,
        TypeCode = "crm.test",
        DateUtc = DateTime.UtcNow,
        Status = DocumentStatus.Posted
    };

    private sealed record CapturedRecord(string Code, ReferenceRegisterRecordWrite Record);

    private sealed class UnknownHandler(ICrmDocumentReaders readers, IDimensionSetService dimensions)
        : CrmReferenceRegisterPostingHandler(readers, dimensions)
    {
        public override string TypeCode => "crm.unknown";
    }

    private sealed class ReaderStub(
        CrmLeadIntakeHead lead,
        CrmLeadQualificationHead qualification,
        CrmLeadConversionHead conversion,
        CrmOpportunityUpdateHead opportunity,
        CrmQuoteHead quote,
        CrmActivityLogHead activity) : ICrmDocumentReaders
    {
        public Task<CrmLeadIntakeHead> ReadLeadIntakeHeadAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(lead);
        public Task<CrmLeadQualificationHead> ReadLeadQualificationHeadAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(qualification);
        public Task<CrmLeadConversionHead> ReadLeadConversionHeadAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(conversion);
        public Task<CrmOpportunityUpdateHead> ReadOpportunityUpdateHeadAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(opportunity);
        public Task<CrmQuoteHead> ReadQuoteHeadAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(quote);
        public Task<IReadOnlyList<CrmQuoteLine>> ReadQuoteLinesAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CrmQuoteLine>>([]);
        public Task<CrmActivityLogHead> ReadActivityLogHeadAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(activity);
    }
}
