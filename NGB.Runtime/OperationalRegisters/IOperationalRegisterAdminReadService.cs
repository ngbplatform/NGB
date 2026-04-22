using NGB.OperationalRegisters.Contracts;

namespace NGB.Runtime.OperationalRegisters;

/// <summary>
/// Runtime-level read facade for Operational Registers admin UX.
///
/// Notes:
/// - Read-only; does not require a UnitOfWork transaction.
/// - Intended for REST/GraphQL endpoints and UI screens.
/// </summary>
public interface IOperationalRegisterAdminReadService
{
    Task<IReadOnlyList<OperationalRegisterAdminListItem>> GetListAsync(CancellationToken ct = default);

    Task<OperationalRegisterAdminDetails?> GetDetailsByIdAsync(Guid registerId, CancellationToken ct = default);

    Task<OperationalRegisterAdminDetails?> GetDetailsByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Physical schema health report for all registers (dynamic per-register tables).
    /// </summary>
    Task<OperationalRegisterPhysicalSchemaHealthReport> GetPhysicalSchemaHealthReportAsync(CancellationToken ct = default);

    /// <summary>
    /// Physical schema health for a single register.
    /// Returns null when register does not exist.
    /// </summary>
    Task<OperationalRegisterPhysicalSchemaHealth?> GetPhysicalSchemaHealthByIdAsync(
        Guid registerId,
        CancellationToken ct = default);

    /// <summary>
    /// Reads the finalization marker for a specific register-month.
    /// Returns null when no marker exists.
    /// </summary>
    Task<OperationalRegisterFinalization?> GetFinalizationAsync(
        Guid registerId,
        DateOnly periodMonth,
        CancellationToken ct = default);

    /// <summary>
    /// Lists dirty finalization markers for a register.
    /// </summary>
    Task<IReadOnlyList<OperationalRegisterFinalization>> GetDirtyFinalizationsAsync(
        Guid registerId,
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Lists blocked finalization markers for a register (e.g. missing projector).
    /// </summary>
    Task<IReadOnlyList<OperationalRegisterFinalization>> GetBlockedFinalizationsAsync(
        Guid registerId,
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Lists dirty finalization markers across all registers.
    /// </summary>
    Task<IReadOnlyList<OperationalRegisterFinalization>> GetDirtyFinalizationsAcrossAllAsync(
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Lists blocked finalization markers across all registers.
    /// </summary>
    Task<IReadOnlyList<OperationalRegisterFinalization>> GetBlockedFinalizationsAcrossAllAsync(
        int limit = 100,
        CancellationToken ct = default);
}
