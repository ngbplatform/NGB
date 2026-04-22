using NGB.ReferenceRegisters.Contracts;

namespace NGB.Runtime.ReferenceRegisters;

/// <summary>
/// Read-only admin services for Reference Registers.
///
/// This is an internal Runtime abstraction used by <see cref="IReferenceRegisterAdminEndpoint"/>.
/// </summary>
public interface IReferenceRegisterAdminReadService
{
    Task<IReadOnlyList<(ReferenceRegisterAdminItem Register, int FieldsCount, int DimensionRulesCount)>> GetListAsync(
        CancellationToken ct = default);

    Task<ReferenceRegisterAdminDetails?> GetDetailsByIdAsync(Guid registerId, CancellationToken ct = default);

    Task<ReferenceRegisterAdminDetails?> GetDetailsByCodeAsync(string code, CancellationToken ct = default);

    Task<ReferenceRegisterPhysicalSchemaHealthReport> GetPhysicalSchemaHealthReportAsync(CancellationToken ct = default);

    Task<ReferenceRegisterPhysicalSchemaHealth?> GetPhysicalSchemaHealthByIdAsync(
        Guid registerId,
        CancellationToken ct = default);
}
