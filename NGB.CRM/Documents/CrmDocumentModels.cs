namespace NGB.CRM.Documents;

public sealed record CrmLeadIntakeHead(
    Guid DocumentId,
    DateOnly DocumentDateUtc,
    string LeadName,
    string? CompanyName,
    string ContactName,
    string? Email,
    string? Phone,
    string? LeadSource,
    string? Industry,
    decimal? EstimatedValue,
    string? Currency,
    string? Notes);

public sealed record CrmLeadQualificationHead(
    Guid DocumentId,
    DateOnly DocumentDateUtc,
    Guid LeadIntakeId,
    string QualificationState,
    int Score,
    string? DisqualificationReason,
    string? Notes);

public sealed record CrmLeadConversionHead(
    Guid DocumentId,
    DateOnly DocumentDateUtc,
    Guid LeadIntakeId,
    Guid AccountId,
    Guid ContactId,
    bool CreateOpportunity,
    string? OpportunityName,
    Guid? StageId,
    decimal? Amount,
    decimal? Probability,
    DateOnly? ExpectedCloseDate,
    string? Currency,
    string? Notes);

public sealed record CrmOpportunityUpdateHead(
    Guid DocumentId,
    DateOnly DocumentDateUtc,
    Guid OpportunityId,
    Guid StageId,
    decimal Amount,
    decimal Probability,
    DateOnly? ExpectedCloseDate,
    string Status,
    string? LossReason,
    string? Notes);

public sealed record CrmQuoteHead(
    Guid DocumentId,
    DateOnly DocumentDateUtc,
    Guid OpportunityId,
    Guid AccountId,
    Guid? ContactId,
    DateOnly ValidUntil,
    string Currency,
    string QuoteStatus,
    decimal Amount,
    string? Notes);

public sealed record CrmQuoteLine(
    Guid DocumentId,
    int Ordinal,
    Guid ProductId,
    string? Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal LineAmount);

public sealed record CrmActivityLogHead(
    Guid DocumentId,
    DateOnly DocumentDateUtc,
    string ActivityType,
    string Subject,
    Guid? LeadIntakeId,
    Guid? AccountId,
    Guid? ContactId,
    Guid? OpportunityId,
    DateTime? DueAtUtc,
    DateTime? CompletedAtUtc,
    string? Outcome,
    string? Notes);
