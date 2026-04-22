namespace NGB.Contracts.Accounting;

public sealed record CloseMonthRequestDto(DateOnly Period);

public sealed record ReopenMonthRequestDto(DateOnly Period, string Reason);

public sealed record PeriodCloseStatusDto(
    DateOnly Period,
    string State,
    bool IsClosed,
    bool HasActivity,
    string? ClosedBy,
    DateTime? ClosedAtUtc,
    bool CanClose,
    bool CanReopen,
    DateOnly? BlockingPeriod,
    string? BlockingReason);

public sealed record PeriodClosingCalendarDto(
    int Year,
    DateOnly YearStartPeriod,
    DateOnly YearEndPeriod,
    DateOnly? EarliestActivityPeriod,
    DateOnly? LatestContiguousClosedPeriod,
    DateOnly? LatestClosedPeriod,
    DateOnly? NextClosablePeriod,
    bool CanCloseAnyMonth,
    bool HasBrokenChain,
    DateOnly? FirstGapPeriod,
    IReadOnlyList<PeriodCloseStatusDto> Months);

public sealed record CloseFiscalYearRequestDto(
    DateOnly FiscalYearEndPeriod,
    Guid RetainedEarningsAccountId);

public sealed record ReopenFiscalYearRequestDto(DateOnly FiscalYearEndPeriod, string Reason);

public sealed record RetainedEarningsAccountOptionDto(Guid AccountId, string Code, string Name, string Display);

public sealed record FiscalYearCloseStatusDto(
    DateOnly FiscalYearEndPeriod,
    DateOnly FiscalYearStartPeriod,
    string State,
    Guid DocumentId,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    bool EndPeriodClosed,
    string? EndPeriodClosedBy,
    DateTime? EndPeriodClosedAtUtc,
    bool CanClose,
    bool CanReopen,
    bool ReopenWillOpenEndPeriod,
    RetainedEarningsAccountOptionDto? ClosedRetainedEarningsAccount,
    DateOnly? BlockingPeriod,
    string? BlockingReason,
    DateOnly? ReopenBlockingPeriod,
    string? ReopenBlockingReason,
    IReadOnlyList<PeriodCloseStatusDto> PriorMonths);
