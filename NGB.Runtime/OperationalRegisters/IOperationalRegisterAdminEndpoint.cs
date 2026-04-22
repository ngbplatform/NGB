namespace NGB.Runtime.OperationalRegisters;

/// <summary>
/// Runtime "endpoint" abstraction for Operational Registers admin UX.
///
/// Intended usage:
/// - Web API / GraphQL controllers can depend on this interface and return the DTOs as-is.
/// - Keeps the HTTP layer thin and prevents leaking persistence models to the outside.
/// </summary>
public interface IOperationalRegisterAdminEndpoint
{
    Task<IReadOnlyList<OperationalRegisterAdminEndpointContracts.RegisterListItemDto>> GetListAsync(
        CancellationToken ct = default);

    Task<OperationalRegisterAdminEndpointContracts.RegisterDetailsDto?> GetDetailsByIdAsync(
        Guid registerId,
        CancellationToken ct = default);

    Task<OperationalRegisterAdminEndpointContracts.RegisterDetailsDto?> GetDetailsByCodeAsync(
        string code,
        CancellationToken ct = default);

    Task<OperationalRegisterAdminEndpointContracts.PhysicalSchemaHealthReportDto> GetPhysicalSchemaHealthReportAsync(
        CancellationToken ct = default);

    Task<OperationalRegisterAdminEndpointContracts.PhysicalSchemaHealthDto?> GetPhysicalSchemaHealthByIdAsync(
        Guid registerId,
        CancellationToken ct = default);

    /// <summary>
    /// Ensures physical per-register tables (movements/turnovers/balances) exist for the given register.
    /// Returns the physical schema health after ensure.
    /// Returns null when register does not exist.
    /// </summary>
    Task<OperationalRegisterAdminEndpointContracts.PhysicalSchemaHealthDto?> EnsurePhysicalSchemaByIdAsync(
        Guid registerId,
        CancellationToken ct = default);

    /// <summary>
    /// Ensures physical per-register tables (movements/turnovers/balances) exist for all known registers.
    /// Returns the physical schema health report after ensure.
    /// </summary>
    Task<OperationalRegisterAdminEndpointContracts.PhysicalSchemaHealthReportDto> EnsurePhysicalSchemaForAllAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Gets the finalization marker for a specific register-month.
    /// Returns null when no marker exists.
    /// </summary>
    Task<OperationalRegisterAdminEndpointContracts.FinalizationDto?> GetFinalizationAsync(
        Guid registerId,
        DateOnly periodMonth,
        CancellationToken ct = default);

    Task<IReadOnlyList<OperationalRegisterAdminEndpointContracts.FinalizationDto>> GetDirtyFinalizationsByIdAsync(
        Guid registerId,
        int limit = 100,
        CancellationToken ct = default);

    Task<IReadOnlyList<OperationalRegisterAdminEndpointContracts.FinalizationDto>> GetBlockedFinalizationsByIdAsync(
        Guid registerId,
        int limit = 100,
        CancellationToken ct = default);

    Task<IReadOnlyList<OperationalRegisterAdminEndpointContracts.FinalizationDto>> GetDirtyFinalizationsAcrossAllAsync(
        int limit = 100,
        CancellationToken ct = default);

    Task<IReadOnlyList<OperationalRegisterAdminEndpointContracts.FinalizationDto>> GetBlockedFinalizationsAcrossAllAsync(
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Marks the register-month as Dirty (admin remediation).
    /// Useful to unblock months marked BlockedNoProjector.
    /// </summary>
    Task MarkFinalizationDirtyAsync(Guid registerId, DateOnly periodMonth, CancellationToken ct = default);

    /// <summary>
    /// Finalizes up to <paramref name="maxItems"/> dirty months across all registers.
    /// Returns the number of months finalized.
    /// </summary>
    Task<int> FinalizeDirtyAsync(int maxItems = 50, CancellationToken ct = default);

    /// <summary>
    /// Finalizes up to <paramref name="maxPeriods"/> dirty months for the given register.
    /// Returns the number of months finalized.
    /// </summary>
    Task<int> FinalizeRegisterDirtyAsync(Guid registerId, int maxPeriods = 50, CancellationToken ct = default);
}
