using NGB.Contracts.Admin;
using NGB.Contracts.Services;

namespace NGB.Application.Abstractions.Services;

public interface IAdminService
{
    Task<MainMenuDto> GetMainMenuAsync(CancellationToken ct);

    Task<ChartOfAccountsMetadataDto> GetChartOfAccountsMetadataAsync(CancellationToken ct);
    Task<ChartOfAccountsPageDto> GetChartOfAccountsPageAsync(ChartOfAccountsPageRequestDto request, CancellationToken ct);
    Task<ChartOfAccountsAccountDto> GetChartOfAccountAsync(Guid accountId, CancellationToken ct);
    Task<IReadOnlyList<LookupItemDto>> GetChartOfAccountsByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct);

    Task<ChartOfAccountsAccountDto> CreateChartOfAccountAsync(
        ChartOfAccountsUpsertRequestDto request,
        CancellationToken ct);
    
    Task<ChartOfAccountsAccountDto> UpdateChartOfAccountAsync(
        Guid accountId,
        ChartOfAccountsUpsertRequestDto request,
        CancellationToken ct);

    Task MarkChartOfAccountForDeletionAsync(Guid accountId, CancellationToken ct);
    Task UnmarkChartOfAccountForDeletionAsync(Guid accountId, CancellationToken ct);
    Task SetChartOfAccountActiveAsync(Guid accountId, bool isActive, CancellationToken ct);
}
