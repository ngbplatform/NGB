namespace NGB.Contracts.Admin;

public sealed record ChartOfAccountsOptionDto(string Value, string Label);

public sealed record ChartOfAccountsCashFlowRoleOptionDto(
    string Value,
    string Label,
    bool SupportsLineCode,
    bool RequiresLineCode);

public sealed record ChartOfAccountsCashFlowLineOptionDto(
    string Value,
    string Label,
    string Section,
    IReadOnlyList<string> AllowedRoles);

public sealed record ChartOfAccountsMetadataDto(
    IReadOnlyList<ChartOfAccountsOptionDto> AccountTypeOptions,
    IReadOnlyList<ChartOfAccountsCashFlowRoleOptionDto> CashFlowRoleOptions,
    IReadOnlyList<ChartOfAccountsCashFlowLineOptionDto> CashFlowLineOptions);

public sealed record ChartOfAccountsPageRequestDto(
    int Offset = 0,
    int Limit = 100,
    string? Search = null,
    IReadOnlyList<string>? AccountTypes = null,
    bool IncludeDeleted = false,
    bool? OnlyActive = null,
    bool? OnlyDeleted = null);

public sealed record ChartOfAccountsAccountDto(
    Guid AccountId,
    string Code,
    string Name,
    string AccountType,
    string? CashFlowRole,
    string? CashFlowLineCode,
    bool IsActive,
    bool IsDeleted,
    bool IsMarkedForDeletion);

public sealed record ChartOfAccountsPageDto(
    IReadOnlyList<ChartOfAccountsAccountDto> Items,
    int Offset,
    int Limit,
    int? Total);

public sealed record ChartOfAccountsUpsertRequestDto(
    string Code,
    string Name,
    string AccountType,
    bool IsActive = true,
    string? CashFlowRole = null,
    string? CashFlowLineCode = null);
