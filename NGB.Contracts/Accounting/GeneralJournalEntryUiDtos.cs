using NGB.Contracts.Metadata;

namespace NGB.Contracts.Accounting;

public sealed record GeneralJournalEntryDimensionValueDto(Guid DimensionId, Guid ValueId, string? Display = null);

public sealed record GeneralJournalEntryLineDto(
    int LineNo,
    int Side,
    Guid AccountId,
    decimal Amount,
    string? Memo,
    Guid DimensionSetId,
    IReadOnlyList<GeneralJournalEntryDimensionValueDto> Dimensions,
    string? AccountDisplay = null);

public sealed record GeneralJournalEntryAllocationDto(
    int EntryNo,
    int DebitLineNo,
    int CreditLineNo,
    decimal Amount);

public sealed record GeneralJournalEntryHeaderDto(
    int JournalType,
    int Source,
    int ApprovalState,
    string? ReasonCode,
    string? Memo,
    string? ExternalReference,
    bool AutoReverse,
    DateOnly? AutoReverseOnUtc,
    Guid? ReversalOfDocumentId,
    string? InitiatedBy,
    DateTime? InitiatedAtUtc,
    string? SubmittedBy,
    DateTime? SubmittedAtUtc,
    string? ApprovedBy,
    DateTime? ApprovedAtUtc,
    string? RejectedBy,
    DateTime? RejectedAtUtc,
    string? RejectReason,
    string? PostedBy,
    DateTime? PostedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string? ReversalOfDocumentDisplay = null);

public sealed record GeneralJournalEntryListItemDto(
    Guid Id,
    DateTime DateUtc,
    string? Number,
    string? Display,
    int DocumentStatus,
    bool IsMarkedForDeletion,
    int JournalType,
    int Source,
    int ApprovalState,
    string? ReasonCode,
    string? Memo,
    string? ExternalReference,
    bool AutoReverse,
    DateOnly? AutoReverseOnUtc,
    Guid? ReversalOfDocumentId,
    string? PostedBy,
    DateTime? PostedAtUtc);

public sealed record GeneralJournalEntryPageDto(
    IReadOnlyList<GeneralJournalEntryListItemDto> Items,
    int Offset,
    int Limit,
    int? Total);

public sealed record GeneralJournalEntryDocumentDto(
    Guid Id,
    string? Display,
    DocumentStatus Status,
    bool IsMarkedForDeletion,
    string? Number = null);

public sealed record GeneralJournalEntryDetailsDto(
    GeneralJournalEntryDocumentDto Document,
    DateTime DateUtc,
    GeneralJournalEntryHeaderDto Header,
    IReadOnlyList<GeneralJournalEntryLineDto> Lines,
    IReadOnlyList<GeneralJournalEntryAllocationDto> Allocations,
    IReadOnlyList<GeneralJournalEntryAccountContextDto>? AccountContexts = null);

public sealed record GeneralJournalEntryDimensionRuleDto(
    Guid DimensionId,
    string DimensionCode,
    int Ordinal,
    bool IsRequired,
    LookupSourceDto? Lookup = null);

public sealed record GeneralJournalEntryAccountContextDto(
    Guid AccountId,
    string Code,
    string Name,
    IReadOnlyList<GeneralJournalEntryDimensionRuleDto> DimensionRules);

public sealed record CreateGeneralJournalEntryDraftRequestDto(DateTime DateUtc);

public sealed record UpdateGeneralJournalEntryHeaderRequestDto(
    string UpdatedBy,
    int? JournalType = null,
    string? ReasonCode = null,
    string? Memo = null,
    string? ExternalReference = null,
    bool? AutoReverse = null,
    DateOnly? AutoReverseOnUtc = null);

public sealed record GeneralJournalEntryLineInputDto(
    int Side,
    Guid AccountId,
    decimal Amount,
    string? Memo,
    IReadOnlyList<GeneralJournalEntryDimensionValueDto>? Dimensions = null);

public sealed record ReplaceGeneralJournalEntryLinesRequestDto(
    string UpdatedBy,
    IReadOnlyList<GeneralJournalEntryLineInputDto> Lines);

public sealed record GeneralJournalEntryRejectRequestDto(string RejectReason);

public sealed record GeneralJournalEntryReverseRequestDto(
    DateTime ReversalDateUtc,
    bool PostImmediately = true);
