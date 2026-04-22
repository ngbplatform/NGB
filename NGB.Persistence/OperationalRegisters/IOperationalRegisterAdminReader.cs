using NGB.OperationalRegisters.Contracts;

namespace NGB.Persistence.OperationalRegisters;

/// <summary>
/// Read-only admin projection for Operational Registers.
///
/// This interface is optimized for admin UX and avoids N+1 queries by providing
/// list and details read models.
/// </summary>
public interface IOperationalRegisterAdminReader
{
    Task<IReadOnlyList<OperationalRegisterAdminListItem>> GetListAsync(CancellationToken ct = default);

    Task<OperationalRegisterAdminDetails?> GetDetailsByIdAsync(Guid registerId, CancellationToken ct = default);

    Task<OperationalRegisterAdminDetails?> GetDetailsByCodeAsync(string code, CancellationToken ct = default);
}
