using NGB.Accounting.Accounts;
using NGB.Accounting.CashFlow;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Admin;
using NGB.Contracts.Services;
using NGB.Persistence.Accounts;
using NGB.Runtime.Accounts;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Admin;

/// <summary>
/// Platform implementation of <see cref="IAdminService"/>.
///
/// Main menu is composed from <see cref="IMainMenuContributor"/> implementations.
/// Chart of Accounts operations delegate to the platform Accounting services.
/// </summary>
public sealed class AdminService(
    IMainMenuService menu,
    IChartOfAccountsAdminService coaAdmin,
    IChartOfAccountsManagementService coaManagement,
    ICashFlowLineRepository cashFlowLines)
    : IAdminService
{
    public Task<MainMenuDto> GetMainMenuAsync(CancellationToken ct) => menu.GetMainMenuAsync(ct);

    public async Task<ChartOfAccountsMetadataDto> GetChartOfAccountsMetadataAsync(CancellationToken ct)
    {
        var accountTypeOptions = Enum.GetValues<AccountType>()
            .Select(x => new ChartOfAccountsOptionDto(x.ToString(), x.ToDisplay()))
            .ToArray();

        var cashFlowRoleOptions = Enum.GetValues<CashFlowRole>()
            .Select(x => new ChartOfAccountsCashFlowRoleOptionDto(
                Value: x == CashFlowRole.None ? string.Empty : x.ToString(),
                Label: x.ToDisplay(),
                SupportsLineCode: CashFlowRoleRules.SupportsLineCode(x),
                RequiresLineCode: CashFlowRoleRules.RequiresLineCode(x)))
            .ToArray();

        var cashFlowLineOptions = (await cashFlowLines.GetAllAsync(ct))
            .Where(x => x.Method == CashFlowMethod.Indirect)
            .Select(x => new ChartOfAccountsCashFlowLineOptionDto(
                Value: x.LineCode,
                Label: x.Label,
                Section: x.Section.ToString(),
                AllowedRoles: CashFlowRoleRules.GetAllowedRoles(x)
                    .Select(role => role.ToString())
                    .ToArray()))
            .ToArray();

        return new ChartOfAccountsMetadataDto(
            AccountTypeOptions: accountTypeOptions,
            CashFlowRoleOptions: cashFlowRoleOptions,
            CashFlowLineOptions: cashFlowLineOptions);
    }

    public async Task<ChartOfAccountsPageDto> GetChartOfAccountsPageAsync(
        ChartOfAccountsPageRequestDto request,
        CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        if (request.Offset < 0)
            throw new NgbArgumentOutOfRangeException(nameof(request.Offset), request.Offset, "Offset must be 0 or greater.");

        if (request.Limit is <= 0 or > 500)
            throw new NgbArgumentOutOfRangeException(nameof(request.Limit), request.Limit, "Limit must be between 1 and 500.");

        var accountTypes = request.AccountTypes is { Count: > 0 }
            ? request.AccountTypes
                .Select(x => ParseAccountType(x, nameof(request.AccountTypes)))
                .Distinct()
                .ToArray()
            : [];

        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();

        var searchAccountTypes = search is null
            ? []
            : Enum.GetValues<AccountType>()
                .Where(type => type.ToString().Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        var result = await coaAdmin.GetPageAsync(
            new ChartOfAccountsAdminPageQuery(
                request.IncludeDeleted,
                request.OnlyDeleted,
                request.OnlyActive,
                accountTypes,
                search,
                searchAccountTypes,
                request.Offset,
                request.Limit),
            ct);

        var page = result.Items
            .Select(Map)
            .ToArray();

        return new ChartOfAccountsPageDto(page, request.Offset, request.Limit, result.Total);
    }

    public async Task<ChartOfAccountsAccountDto> GetChartOfAccountAsync(Guid accountId, CancellationToken ct)
    {
        var item = await coaAdmin.GetByIdAsync(accountId, ct);
        if (item is null)
            throw new AccountNotFoundException(accountId);

        return Map(item);
    }

    public async Task<IReadOnlyList<LookupItemDto>> GetChartOfAccountsByIdsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken ct)
    {
        if (ids is null)
            throw new NgbArgumentRequiredException(nameof(ids));

        if (ids.Count == 0)
            return [];

        var uniqIds = ids.Where(static id => id != Guid.Empty).Distinct().ToArray();
        if (uniqIds.Length == 0)
            return [];

        var items = await coaAdmin.GetByIdsAsync(uniqIds, ct);
        return items
            .Select(static item => new LookupItemDto(item.Account.Id, $"{item.Account.Code} — {item.Account.Name}"))
            .ToArray();
    }

    public async Task<ChartOfAccountsAccountDto> CreateChartOfAccountAsync(
        ChartOfAccountsUpsertRequestDto request,
        CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        var type = ParseAccountType(request.AccountType, nameof(request.AccountType));

        var id = await coaManagement.CreateAsync(new CreateAccountRequest(
            Code: request.Code,
            Name: request.Name,
            Type: type,
            IsActive: request.IsActive,
            CashFlowRole: ParseCashFlowRole(request.CashFlowRole, nameof(request.CashFlowRole)),
            CashFlowLineCode: request.CashFlowLineCode), ct);

        return await GetChartOfAccountAsync(id, ct);
    }

    public async Task<ChartOfAccountsAccountDto> UpdateChartOfAccountAsync(
        Guid accountId,
        ChartOfAccountsUpsertRequestDto request,
        CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        var type = ParseAccountType(request.AccountType, nameof(request.AccountType));

        await coaManagement.UpdateAsync(new UpdateAccountRequest(
            AccountId: accountId,
            Code: request.Code,
            Name: request.Name,
            Type: type,
            IsActive: request.IsActive,
            CashFlowRole: ParseCashFlowRole(request.CashFlowRole, nameof(request.CashFlowRole)),
            CashFlowLineCode: request.CashFlowLineCode), ct);

        return await GetChartOfAccountAsync(accountId, ct);
    }

    public Task MarkChartOfAccountForDeletionAsync(Guid accountId, CancellationToken ct)
        => coaManagement.MarkForDeletionAsync(accountId, ct);

    public Task UnmarkChartOfAccountForDeletionAsync(Guid accountId, CancellationToken ct)
        => coaManagement.UnmarkForDeletionAsync(accountId, ct);

    public Task SetChartOfAccountActiveAsync(Guid accountId, bool isActive, CancellationToken ct)
        => coaManagement.SetActiveAsync(accountId, isActive, ct);

    private static AccountType ParseAccountType(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new NgbArgumentRequiredException(paramName);

        if (!Enum.TryParse<AccountType>(value, ignoreCase: true, out var parsed))
            throw new NgbArgumentInvalidException(paramName, $"Unknown account type '{value}'.");

        return parsed;
    }

    private static CashFlowRole? ParseCashFlowRole(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!Enum.TryParse<CashFlowRole>(value, ignoreCase: true, out var parsed))
            throw new NgbArgumentInvalidException(paramName, $"Unknown cash flow role '{value}'.");

        return parsed;
    }

    private static ChartOfAccountsAccountDto Map(ChartOfAccountsAdminItem item)
        => new(
            AccountId: item.Account.Id,
            Code: item.Account.Code,
            Name: item.Account.Name,
            AccountType: item.Account.Type.ToString(),
            CashFlowRole: item.Account.CashFlowRole == CashFlowRole.None ? null : item.Account.CashFlowRole.ToString(),
            CashFlowLineCode: item.Account.CashFlowLineCode,
            IsActive: item.IsActive,
            IsDeleted: item.IsDeleted,
            IsMarkedForDeletion: item.IsDeleted);
}
