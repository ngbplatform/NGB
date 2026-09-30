using FluentAssertions;
using NGB.Core.Documents;
using NGB.CRM.Documents;
using NGB.CRM.Runtime.Documents.Validation;
using NGB.Tools.Exceptions;

namespace NGB.CRM.Runtime.Tests.Documents;

public sealed class CrmPostValidatorsFullCoverageTests
{
    private readonly Guid _documentId = Guid.CreateVersion7();

    [Fact]
    public void TypeCodes_MatchTheirDocuments()
    {
        var readers = new ReaderStub();

        new LeadIntakePostValidator(readers).TypeCode.Should().Be(CrmCodes.LeadIntake);
        new LeadQualificationPostValidator(readers).TypeCode.Should().Be(CrmCodes.LeadQualification);
        new LeadConversionPostValidator(readers).TypeCode.Should().Be(CrmCodes.LeadConversion);
        new OpportunityUpdatePostValidator(readers).TypeCode.Should().Be(CrmCodes.OpportunityUpdate);
        new QuotePostValidator(readers).TypeCode.Should().Be(CrmCodes.Quote);
        new ActivityLogPostValidator(readers).TypeCode.Should().Be(CrmCodes.ActivityLog);
    }

    [Theory]
    [InlineData("lead")]
    [InlineData("contact")]
    [InlineData("value")]
    public async Task LeadIntake_RejectsEachInvalidField(string field)
    {
        var head = Lead();
        head = field switch
        {
            "lead" => head with { LeadName = " " },
            "contact" => head with { ContactName = "" },
            _ => head with { EstimatedValue = -0.01m }
        };
        var act = () => new LeadIntakePostValidator(new ReaderStub { LeadIntake = head })
            .ValidateBeforePostAsync(Document(), CancellationToken.None);

        await act.Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public async Task LeadIntake_AcceptsNullAndZeroEstimatedValue(int? value)
    {
        await new LeadIntakePostValidator(new ReaderStub { LeadIntake = Lead(estimatedValue: value) })
            .ValidateBeforePostAsync(Document(), CancellationToken.None);
    }

    [Theory]
    [InlineData(-1, "Qualified", null)]
    [InlineData(101, "Qualified", null)]
    [InlineData(50, "disqualified", " ")]
    public async Task Qualification_RejectsScoreAndConditionalReason(int score, string state, string? reason)
    {
        var act = () => new LeadQualificationPostValidator(new ReaderStub
            {
                Qualification = Qualification(score, state, reason)
            })
            .ValidateBeforePostAsync(Document(), CancellationToken.None);

        await act.Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Theory]
    [InlineData(0, "Qualified", null)]
    [InlineData(100, "Disqualified", "Not a fit")]
    public async Task Qualification_AcceptsScoreBoundariesAndReasonPaths(int score, string state, string? reason)
    {
        await new LeadQualificationPostValidator(new ReaderStub
            {
                Qualification = Qualification(score, state, reason)
            })
            .ValidateBeforePostAsync(Document(), CancellationToken.None);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("contact")]
    [InlineData("name")]
    [InlineData("stage")]
    [InlineData("probability-low")]
    [InlineData("probability-high")]
    [InlineData("amount")]
    public async Task Conversion_RejectsEachInvalidRequiredOrNumericField(string scenario)
    {
        var head = Conversion();
        head = scenario switch
        {
            "account" => head with { AccountId = null },
            "contact" => head with { ContactId = null },
            "name" => head with { OpportunityName = " " },
            "stage" => head with { StageId = null },
            "probability-low" => head with { Probability = -0.01m },
            "probability-high" => head with { Probability = 100.01m },
            _ => head with { Amount = -0.01m }
        };
        var act = () => new LeadConversionPostValidator(new ReaderStub { Conversion = head })
            .ValidateBeforePostAsync(Document(), CancellationToken.None);

        await act.Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Fact]
    public async Task Conversion_WhenOpportunityIsDisabled_SkipsOpportunityFields()
    {
        var head = Conversion() with
        {
            CreateOpportunity = false,
            OpportunityName = null,
            StageId = null,
            Amount = -1m,
            Probability = -1m
        };

        await new LeadConversionPostValidator(new ReaderStub { Conversion = head })
            .ValidateBeforePostAsync(Document(), CancellationToken.None);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 0)]
    [InlineData(100, 10)]
    public async Task Conversion_AcceptsNullableAndBoundaryNumericValues(int? probability, int? amount)
    {
        await new LeadConversionPostValidator(new ReaderStub
            {
                Conversion = Conversion() with { Probability = probability, Amount = amount }
            })
            .ValidateBeforePostAsync(Document(), CancellationToken.None);
    }

    [Theory]
    [InlineData("amount-low")]
    [InlineData("probability-low")]
    [InlineData("probability-high")]
    [InlineData("lost-reason")]
    public async Task OpportunityUpdate_RejectsInvalidNumericAndLostState(string scenario)
    {
        var head = Opportunity();
        head = scenario switch
        {
            "amount-low" => head with { Amount = -0.01m },
            "probability-low" => head with { Probability = -0.01m },
            "probability-high" => head with { Probability = 100.01m },
            _ => head with { Status = "lost", LossReason = " " }
        };
        var act = () => new OpportunityUpdatePostValidator(new ReaderStub { Opportunity = head })
            .ValidateBeforePostAsync(Document(), CancellationToken.None);

        await act.Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Theory]
    [InlineData("Open", null, 0, 0)]
    [InlineData("Lost", "Budget", 100, 100)]
    public async Task OpportunityUpdate_AcceptsBoundariesAndBothStatusPaths(
        string status, string? reason, int probability, int amount)
    {
        await new OpportunityUpdatePostValidator(new ReaderStub
            {
                Opportunity = Opportunity() with
                {
                    Status = status,
                    LossReason = reason,
                    Probability = probability,
                    Amount = amount
                }
            })
            .ValidateBeforePostAsync(Document(), CancellationToken.None);
    }

    [Theory]
    [InlineData("date")]
    [InlineData("amount")]
    [InlineData("empty")]
    [InlineData("ordinal")]
    [InlineData("quantity")]
    [InlineData("price")]
    [InlineData("discount-low")]
    [InlineData("discount-high")]
    [InlineData("line-amount")]
    public async Task Quote_RejectsEveryInvalidHeadAndLineBoundary(string scenario)
    {
        var head = Quote();
        IReadOnlyList<CrmQuoteLine> lines = [Line()];
        switch (scenario)
        {
            case "date": head = head with { ValidUntil = head.DocumentDateUtc.AddDays(-1) }; break;
            case "amount": head = head with { Amount = -0.01m }; break;
            case "empty": lines = []; break;
            case "ordinal": lines = [Line() with { Ordinal = 0 }]; break;
            case "quantity": lines = [Line() with { Quantity = 0m }]; break;
            case "price": lines = [Line() with { UnitPrice = -0.01m }]; break;
            case "discount-low": lines = [Line() with { DiscountPercent = -0.01m }]; break;
            case "discount-high": lines = [Line() with { DiscountPercent = 100.01m }]; break;
            default: lines = [Line() with { LineAmount = -0.01m }]; break;
        }
        var act = () => new QuotePostValidator(new ReaderStub { Quote = head, QuoteLines = lines })
            .ValidateBeforePostAsync(Document(), CancellationToken.None);

        await act.Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Fact]
    public async Task Quote_AcceptsInclusiveDateAndNumericBoundariesAndAllLines()
    {
        var head = Quote() with { ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow), Amount = 0m };
        var lines = new[]
        {
            Line() with { UnitPrice = 0m, DiscountPercent = 0m, LineAmount = 0m },
            Line() with { Ordinal = 2, DiscountPercent = 100m }
        };

        await new QuotePostValidator(new ReaderStub { Quote = head, QuoteLines = lines })
            .ValidateBeforePostAsync(Document(), CancellationToken.None);
    }

    [Fact]
    public async Task Activity_RejectsBlankSubjectAndMissingRelation()
    {
        var blank = () => new ActivityLogPostValidator(new ReaderStub
            {
                Activity = Activity() with { Subject = " " }
            }).ValidateBeforePostAsync(Document(), CancellationToken.None);
        await blank.Should().ThrowAsync<NgbArgumentInvalidException>();

        var unrelated = () => new ActivityLogPostValidator(new ReaderStub { Activity = Activity() })
            .ValidateBeforePostAsync(Document(), CancellationToken.None);
        await unrelated.Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Theory]
    [InlineData("lead")]
    [InlineData("account")]
    [InlineData("contact")]
    [InlineData("opportunity")]
    public async Task Activity_AcceptsEachRelationIndependently(string relation)
    {
        var id = Guid.CreateVersion7();
        var head = Activity() with
        {
            LeadIntakeId = relation == "lead" ? id : null,
            AccountId = relation == "account" ? id : null,
            ContactId = relation == "contact" ? id : null,
            OpportunityId = relation == "opportunity" ? id : null
        };

        await new ActivityLogPostValidator(new ReaderStub { Activity = head })
            .ValidateBeforePostAsync(Document(), CancellationToken.None);
    }

    private DocumentRecord Document() => new()
    {
        Id = _documentId,
        TypeCode = "crm.test",
        DateUtc = DateTime.UtcNow,
        Status = DocumentStatus.Draft
    };

    private CrmLeadIntakeHead Lead(decimal? estimatedValue = null) =>
        new(_documentId, DateOnly.FromDateTime(DateTime.UtcNow), "Lead", null, "Contact", null, null, null, null,
            estimatedValue, null, null);

    private CrmLeadQualificationHead Qualification(int score, string state, string? reason) =>
        new(_documentId, DateOnly.FromDateTime(DateTime.UtcNow), Guid.CreateVersion7(), state, score, reason, null);

    private CrmLeadConversionHead Conversion() =>
        new(_documentId, DateOnly.FromDateTime(DateTime.UtcNow), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), true, "Opportunity", Guid.CreateVersion7(), 10m, 50m, null, "USD", null);

    private CrmOpportunityUpdateHead Opportunity() =>
        new(_documentId, DateOnly.FromDateTime(DateTime.UtcNow), Guid.CreateVersion7(), Guid.CreateVersion7(),
            10m, 50m, null, "Open", null, null);

    private CrmQuoteHead Quote() =>
        new(_documentId, DateOnly.FromDateTime(DateTime.UtcNow), Guid.CreateVersion7(), Guid.CreateVersion7(), null,
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), "USD", "Draft", 10m, null);

    private CrmQuoteLine Line() =>
        new(_documentId, 1, Guid.CreateVersion7(), null, 1m, 10m, 10m, 9m);

    private CrmActivityLogHead Activity() =>
        new(_documentId, DateOnly.FromDateTime(DateTime.UtcNow), "Call", "Subject", null, null, null, null, null,
            null, null, null);

    private sealed class ReaderStub : ICrmDocumentReaders
    {
        public CrmLeadIntakeHead LeadIntake { get; init; } = null!;
        public CrmLeadQualificationHead Qualification { get; init; } = null!;
        public CrmLeadConversionHead Conversion { get; init; } = null!;
        public CrmOpportunityUpdateHead Opportunity { get; init; } = null!;
        public CrmQuoteHead Quote { get; init; } = null!;
        public IReadOnlyList<CrmQuoteLine> QuoteLines { get; init; } = [];
        public CrmActivityLogHead Activity { get; init; } = null!;

        public Task<CrmLeadIntakeHead> ReadLeadIntakeHeadAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(LeadIntake);
        public Task<CrmLeadQualificationHead> ReadLeadQualificationHeadAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(Qualification);
        public Task<CrmLeadConversionHead> ReadLeadConversionHeadAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(Conversion);
        public Task<CrmOpportunityUpdateHead> ReadOpportunityUpdateHeadAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(Opportunity);
        public Task<CrmQuoteHead> ReadQuoteHeadAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(Quote);
        public Task<IReadOnlyList<CrmQuoteLine>> ReadQuoteLinesAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(QuoteLines);
        public Task<CrmActivityLogHead> ReadActivityLogHeadAsync(Guid documentId, CancellationToken ct = default) =>
            Task.FromResult(Activity);
    }
}
