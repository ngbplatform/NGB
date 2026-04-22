using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Accounting;

namespace NGB.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/accounting/period-closing")]
public sealed class AccountingPeriodClosingController(IPeriodClosingUiService service) : ControllerBase
{
    [HttpGet("month")]
    public Task<PeriodCloseStatusDto> GetMonthStatus([FromQuery] DateOnly period, CancellationToken ct)
        => service.GetMonthStatusAsync(period, ct);

    [HttpPost("month/close")]
    public Task<PeriodCloseStatusDto> CloseMonth([FromBody] CloseMonthRequestDto request, CancellationToken ct)
        => service.CloseMonthAsync(request, ct);

    [HttpPost("month/reopen")]
    public Task<PeriodCloseStatusDto> ReopenMonth([FromBody] ReopenMonthRequestDto request, CancellationToken ct)
        => service.ReopenMonthAsync(request, ct);

    [HttpGet("calendar")]
    public Task<PeriodClosingCalendarDto> GetCalendar([FromQuery] int year, CancellationToken ct)
        => service.GetCalendarAsync(year, ct);

    [HttpGet("fiscal-year")]
    public Task<FiscalYearCloseStatusDto> GetFiscalYearStatus(
        [FromQuery] DateOnly fiscalYearEndPeriod,
        CancellationToken ct)
        => service.GetFiscalYearStatusAsync(fiscalYearEndPeriod, ct);

    [HttpPost("fiscal-year/close")]
    public Task<FiscalYearCloseStatusDto> CloseFiscalYear(
        [FromBody] CloseFiscalYearRequestDto request,
        CancellationToken ct)
        => service.CloseFiscalYearAsync(request, ct);

    [HttpPost("fiscal-year/reopen")]
    public Task<FiscalYearCloseStatusDto> ReopenFiscalYear(
        [FromBody] ReopenFiscalYearRequestDto request,
        CancellationToken ct)
        => service.ReopenFiscalYearAsync(request, ct);

    [HttpGet("retained-earnings-accounts")]
    public Task<IReadOnlyList<RetainedEarningsAccountOptionDto>> SearchRetainedEarningsAccounts(
        [FromQuery(Name = "q")] string? query = null,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
        => service.SearchRetainedEarningsAccountsAsync(query, limit, ct);
}
