namespace NGB.PropertyManagement.Runtime.Policy;

public interface IPropertyManagementBankAccountReader
{
    Task<PropertyManagementBankAccount?> TryGetAsync(Guid bankAccountId, CancellationToken ct = default);
    Task<PropertyManagementBankAccount> GetRequiredAsync(Guid bankAccountId, CancellationToken ct = default);
    Task<PropertyManagementBankAccount?> TryGetDefaultAsync(CancellationToken ct = default);
}

public sealed record PropertyManagementBankAccount(
    Guid BankAccountId,
    string? Display,
    Guid GlAccountId,
    bool IsDefault,
    bool IsDeleted);
