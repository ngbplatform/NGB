namespace NGB.Runtime.ReferenceRegisters;

/// <summary>
/// Runtime "endpoint" abstraction for Reference Registers admin UX.
///
/// Intended usage:
/// - Web API / GraphQL controllers can depend on this interface and return the DTOs as-is.
/// - Keeps the HTTP layer thin and prevents leaking persistence models to the outside.
/// </summary>
public interface IReferenceRegisterAdminEndpoint
{
    Task<IReadOnlyList<ReferenceRegisterAdminEndpointContracts.RegisterListItemDto>> GetListAsync(
        CancellationToken ct = default);

    Task<ReferenceRegisterAdminEndpointContracts.RegisterDetailsDto?> GetDetailsByIdAsync(
        Guid registerId,
        CancellationToken ct = default);

    Task<ReferenceRegisterAdminEndpointContracts.RegisterDetailsDto?> GetDetailsByCodeAsync(
        string code,
        CancellationToken ct = default);

    Task<ReferenceRegisterAdminEndpointContracts.PhysicalSchemaHealthReportDto> GetPhysicalSchemaHealthReportAsync(
        CancellationToken ct = default);

    Task<ReferenceRegisterAdminEndpointContracts.PhysicalSchemaHealthDto?> GetPhysicalSchemaHealthByIdAsync(
        Guid registerId,
        CancellationToken ct = default);

    /// <summary>
    /// Ensures physical per-register records table exists for the given register.
    /// Returns the physical schema health after ensure.
    /// Returns null when register does not exist.
    /// </summary>
    Task<ReferenceRegisterAdminEndpointContracts.PhysicalSchemaHealthDto?> EnsurePhysicalSchemaByIdAsync(
        Guid registerId,
        CancellationToken ct = default);

    /// <summary>
    /// Ensures physical per-register records tables exist for all known registers.
    /// Returns the physical schema health report after ensure.
    /// </summary>
    Task<ReferenceRegisterAdminEndpointContracts.PhysicalSchemaHealthReportDto> EnsurePhysicalSchemaForAllAsync(
        CancellationToken ct = default);
}
