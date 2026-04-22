namespace NGB.Runtime.OperationalRegisters;

/// <summary>
/// Orchestrates operational register month finalization.
/// </summary>
public interface IOperationalRegisterFinalizationRunner
{
    /// <summary>
    /// Finalizes up to <paramref name="maxItems"/> dirty register-months.
    /// Returns the number of months finalized.
    /// </summary>
    Task<int> FinalizeDirtyAsync(
        int maxItems = 50,
        bool manageTransaction = true,
        CancellationToken ct = default);

    /// <summary>
    /// Finalizes up to <paramref name="maxPeriods"/> dirty months for a specific register.
    /// Returns the number of months finalized.
    /// </summary>
    Task<int> FinalizeRegisterDirtyAsync(
        Guid registerId,
        int maxPeriods = 50,
        bool manageTransaction = true,
        CancellationToken ct = default);
}
