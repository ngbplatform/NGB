using NGB.ReferenceRegisters.Contracts;

namespace NGB.Persistence.ReferenceRegisters;

/// <summary>
/// Persistence boundary for Reference Register registry.
///
/// Table: reference_registers
/// </summary>
public interface IReferenceRegisterRepository
{
    Task<IReadOnlyList<ReferenceRegisterAdminItem>> GetAllAsync(CancellationToken ct = default);

    Task<ReferenceRegisterAdminItem?> GetByIdAsync(Guid registerId, CancellationToken ct = default);

    Task<IReadOnlyList<ReferenceRegisterAdminItem>> GetByIdsAsync(
        IReadOnlyCollection<Guid> registerIds,
        CancellationToken ct = default);

    Task<ReferenceRegisterAdminItem?> GetByCodeAsync(string code, CancellationToken ct = default);

    Task<ReferenceRegisterAdminItem?> GetByTableCodeAsync(string tableCode, CancellationToken ct = default);

    Task UpsertAsync(ReferenceRegisterUpsert register, DateTime nowUtc, CancellationToken ct = default);
}
