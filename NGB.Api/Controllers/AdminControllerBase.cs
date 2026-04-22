using Microsoft.AspNetCore.Mvc;
using NGB.Contracts.Admin;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Services;

namespace NGB.Api.Controllers;

public abstract class AdminControllerBase(IAdminService service) : ControllerBase
{
    [HttpGet("~/api/main-menu")]
    public Task<MainMenuDto> GetMainMenu(CancellationToken ct) => service.GetMainMenuAsync(ct);

    [HttpGet("~/api/chart-of-accounts/metadata")]
    public Task<ChartOfAccountsMetadataDto> GetChartOfAccountsMetadata(CancellationToken ct)
        => service.GetChartOfAccountsMetadataAsync(ct);

    [HttpGet("~/api/chart-of-accounts")]
    public Task<ChartOfAccountsPageDto> GetChartOfAccounts(
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 100,
        [FromQuery] string? search = null,
        [FromQuery] string[]? accountTypes = null,
        [FromQuery] bool includeDeleted = false,
        [FromQuery] bool? onlyActive = null,
        [FromQuery] bool? onlyDeleted = null,
        CancellationToken ct = default)
        => service.GetChartOfAccountsPageAsync(new ChartOfAccountsPageRequestDto(
            Offset: offset,
            Limit: limit,
            Search: search,
            AccountTypes: accountTypes,
            IncludeDeleted: includeDeleted,
            OnlyActive: onlyActive,
            OnlyDeleted: onlyDeleted), ct);

    [HttpGet("~/api/chart-of-accounts/{accountId:guid}")]
    public Task<ChartOfAccountsAccountDto> GetChartOfAccount([FromRoute] Guid accountId, CancellationToken ct)
        => service.GetChartOfAccountAsync(accountId, ct);

    [HttpPost("~/api/chart-of-accounts/by-ids")]
    public Task<IReadOnlyList<LookupItemDto>> GetChartOfAccountsByIds(
        [FromBody] ByIdsRequestDto request,
        CancellationToken ct)
        => service.GetChartOfAccountsByIdsAsync(request.Ids, ct);

    [HttpPost("~/api/chart-of-accounts")]
    public Task<ChartOfAccountsAccountDto> CreateChartOfAccount(
        [FromBody] ChartOfAccountsUpsertRequestDto request,
        CancellationToken ct)
        => service.CreateChartOfAccountAsync(request, ct);

    [HttpPut("~/api/chart-of-accounts/{accountId:guid}")]
    public Task<ChartOfAccountsAccountDto> UpdateChartOfAccount(
        [FromRoute] Guid accountId,
        [FromBody] ChartOfAccountsUpsertRequestDto request,
        CancellationToken ct)
        => service.UpdateChartOfAccountAsync(accountId, request, ct);

    [HttpPost("~/api/chart-of-accounts/{accountId:guid}/mark-for-deletion")]
    public async Task<IActionResult> MarkChartOfAccountForDeletion([FromRoute] Guid accountId, CancellationToken ct)
    {
        await service.MarkChartOfAccountForDeletionAsync(accountId, ct);
        return NoContent();
    }

    [HttpPost("~/api/chart-of-accounts/{accountId:guid}/unmark-for-deletion")]
    public async Task<IActionResult> UnmarkChartOfAccountForDeletion([FromRoute] Guid accountId, CancellationToken ct)
    {
        await service.UnmarkChartOfAccountForDeletionAsync(accountId, ct);
        return NoContent();
    }

    [HttpPost("~/api/chart-of-accounts/{accountId:guid}/set-active")]
    public async Task<IActionResult> SetChartOfAccountActive(
        [FromRoute] Guid accountId,
        [FromQuery] bool isActive,
        CancellationToken ct)
    {
        await service.SetChartOfAccountActiveAsync(accountId, isActive, ct);
        return NoContent();
    }
}
