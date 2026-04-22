using NGB.Contracts.Accounting;

namespace NGB.Application.Abstractions.Services;

public interface IPeriodClosingUiService
{
    Task<PeriodCloseStatusDto> GetMonthStatusAsync(DateOnly period, CancellationToken ct);

    Task<PeriodCloseStatusDto> CloseMonthAsync(CloseMonthRequestDto request, CancellationToken ct);

    Task<PeriodCloseStatusDto> ReopenMonthAsync(ReopenMonthRequestDto request, CancellationToken ct);

    Task<PeriodClosingCalendarDto> GetCalendarAsync(int year, CancellationToken ct);

    Task<FiscalYearCloseStatusDto> GetFiscalYearStatusAsync(DateOnly fiscalYearEndPeriod, CancellationToken ct);

    Task<FiscalYearCloseStatusDto> CloseFiscalYearAsync(CloseFiscalYearRequestDto request, CancellationToken ct);

    Task<FiscalYearCloseStatusDto> ReopenFiscalYearAsync(ReopenFiscalYearRequestDto request, CancellationToken ct);

    Task<IReadOnlyList<RetainedEarningsAccountOptionDto>> SearchRetainedEarningsAccountsAsync(
        string? query,
        int limit,
        CancellationToken ct);
}
