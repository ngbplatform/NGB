using NGB.OperationalRegisters.Contracts;

namespace NGB.Runtime.OperationalRegisters;

/// <summary>
/// Runtime-level maintenance facade for Operational Registers admin UX.
///
/// Responsibilities:
/// - Create/repair physical per-register tables (movements/turnovers/balances) without writing any movements.
/// - Provide a single, transactionally safe entry point for "Ensure physical schema" UX actions.
/// </summary>
public interface IOperationalRegisterAdminMaintenanceService
{
    /// <summary>
    /// Ensures physical tables for a single register.
    /// Returns null when register does not exist.
    /// </summary>
    Task<OperationalRegisterPhysicalSchemaHealth?> EnsurePhysicalSchemaByIdAsync(
        Guid registerId,
        CancellationToken ct = default);

    /// <summary>
    /// Ensures physical tables for all known registers.
    /// Returns the physical schema health report after ensure.
    /// </summary>
    Task<OperationalRegisterPhysicalSchemaHealthReport> EnsurePhysicalSchemaForAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Marks a specific register-month as Dirty (admin remediation).
    /// Useful to unblock months marked BlockedNoProjector after installing a projector.
    /// </summary>
    Task MarkFinalizationDirtyAsync(Guid registerId, DateOnly periodMonth, CancellationToken ct = default);

    /// <summary>
    /// Runs the Operational Register finalization runner on up to <paramref name="maxItems"/> dirty months.
    /// Returns the number of months finalized.
    /// </summary>
    Task<int> FinalizeDirtyAsync(int maxItems = 50, CancellationToken ct = default);

    /// <summary>
    /// Runs the Operational Register finalization runner on up to <paramref name="maxPeriods"/> dirty months for a register.
    /// Returns the number of months finalized.
    /// </summary>
    Task<int> FinalizeRegisterDirtyAsync(Guid registerId, int maxPeriods = 50, CancellationToken ct = default);
}
