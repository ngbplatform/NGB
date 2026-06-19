using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Accounting;
using NGB.Core.Security;
using NGB.Runtime.Security;

namespace NGB.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/accounting/period-closing")]
public sealed class AccountingPeriodClosingController(IPeriodClosingUiService service, INgbAccessChecker access)
    : ControllerBase
{
    [HttpGet("month")]
    public async Task<PeriodCloseStatusDto> GetMonthStatus([FromQuery] DateOnly period, CancellationToken ct)
    {
        await RequireAsync(NgbPermissionActions.View, ct);
        return await service.GetMonthStatusAsync(period, ct);
    }

    [HttpPost("month/close")]
    public async Task<PeriodCloseStatusDto> CloseMonth([FromBody] CloseMonthRequestDto request, CancellationToken ct)
    {
        await RequireAsync(NgbPermissionActions.CloseMonth, ct);
        return await service.CloseMonthAsync(request, ct);
    }

    [HttpPost("month/reopen")]
    public async Task<PeriodCloseStatusDto> ReopenMonth([FromBody] ReopenMonthRequestDto request, CancellationToken ct)
    {
        await RequireAsync(NgbPermissionActions.ReopenMonth, ct);
        return await service.ReopenMonthAsync(request, ct);
    }

    [HttpGet("calendar")]
    public async Task<PeriodClosingCalendarDto> GetCalendar([FromQuery] int year, CancellationToken ct)
    {
        await RequireAsync(NgbPermissionActions.View, ct);
        return await service.GetCalendarAsync(year, ct);
    }

    [HttpGet("fiscal-year")]
    public async Task<FiscalYearCloseStatusDto> GetFiscalYearStatus(
        [FromQuery] DateOnly fiscalYearEndPeriod,
        CancellationToken ct)
    {
        await RequireAsync(NgbPermissionActions.View, ct);
        return await service.GetFiscalYearStatusAsync(fiscalYearEndPeriod, ct);
    }

    [HttpPost("fiscal-year/close")]
    public async Task<FiscalYearCloseStatusDto> CloseFiscalYear(
        [FromBody] CloseFiscalYearRequestDto request,
        CancellationToken ct)
    {
        await RequireAsync(NgbPermissionActions.CloseFiscalYear, ct);
        return await service.CloseFiscalYearAsync(request, ct);
    }

    [HttpPost("fiscal-year/reopen")]
    public async Task<FiscalYearCloseStatusDto> ReopenFiscalYear(
        [FromBody] ReopenFiscalYearRequestDto request,
        CancellationToken ct)
    {
        await RequireAsync(NgbPermissionActions.ReopenFiscalYear, ct);
        return await service.ReopenFiscalYearAsync(request, ct);
    }

    [HttpGet("retained-earnings-accounts")]
    public async Task<IReadOnlyList<RetainedEarningsAccountOptionDto>> SearchRetainedEarningsAccounts(
        [FromQuery(Name = "q")] string? query = null,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        await RequireAsync(NgbPermissionActions.View, ct);
        return await service.SearchRetainedEarningsAccountsAsync(query, limit, ct);
    }

    private Task RequireAsync(string action, CancellationToken ct)
        => access.RequireAsync(
            NgbResourceKinds.Admin,
            NgbPermissionResources.PeriodClosing,
            action,
            ct);
}
