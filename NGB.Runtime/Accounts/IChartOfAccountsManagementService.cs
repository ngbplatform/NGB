namespace NGB.Runtime.Accounts;

public interface IChartOfAccountsManagementService
{
    Task<Guid> CreateAsync(CreateAccountRequest request, CancellationToken ct = default);

    Task UpdateAsync(UpdateAccountRequest request, CancellationToken ct = default);

    Task SetActiveAsync(Guid accountId, bool isActive, CancellationToken ct = default);

    Task MarkForDeletionAsync(Guid accountId, CancellationToken ct = default);

    Task UnmarkForDeletionAsync(Guid accountId, CancellationToken ct = default);
}
