using NGB.Core.Dimensions;

namespace NGB.Persistence.OperationalRegisters;

/// <summary>
/// Read-side boundary for computing a single resource net amount for one DimensionSet in an Operational Register.
///
/// Motivation:
/// - Some posting-time validations (e.g. receivables open-items apply) need an up-to-date net amount
///   inside the same transaction as posting, without relying on monthly projections.
/// - The underlying movements tables are per-register (opreg_*__movements) and use storno semantics.
///
/// Notes:
/// - Implementations should return 0 when the physical movements table does not exist yet.
/// - Implementations must be safe against SQL injection (identifiers are dynamic).
/// </summary>
public interface IOperationalRegisterResourceNetReader
{
    /// <summary>
    /// Computes net amount for a single decimal resource:
    /// <c>SUM(non-storno) - SUM(storno)</c> for the specified DimensionSet.
    /// </summary>
    Task<decimal> GetNetByDimensionSetAsync(
        Guid registerId,
        Guid dimensionSetId,
        string resourceColumnCode,
        CancellationToken ct = default);

    /// <summary>
    /// Computes the current net amount for movements whose dimension set contains every
    /// requested dimension/value pair. The aggregation must execute completely in the database;
    /// implementations must not materialize movement rows or dimension display metadata.
    /// </summary>
    Task<decimal> GetNetByDimensionsAsync(
        Guid registerId,
        IReadOnlyList<DimensionValue> dimensions,
        string resourceColumnCode,
        CancellationToken ct = default);
}
