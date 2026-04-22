namespace NGB.Runtime.OperationalRegisters.Projections;

/// <summary>
/// Module-provided projector for an operational register.
///
/// The projector rebuilds derived projections (e.g. turnovers/balances) for a given month
/// based on movements stored in <c>opreg_*__movements</c>.
///
/// Resolution:
/// - Projectors are resolved by <see cref="RegisterCodeNorm"/> (lower(trim(code))).
/// - At most one projector per register code_norm is allowed.
/// </summary>
public interface IOperationalRegisterMonthProjector
{
    /// <summary>
    /// Register code normalized the same way as DB generated column <c>code_norm</c>.
    /// </summary>
    string RegisterCodeNorm { get; }

    /// <summary>
    /// Rebuilds projections for the given register-month.
    /// Must run inside the same transaction as the finalization marker update.
    /// </summary>
    Task RebuildMonthAsync(OperationalRegisterMonthProjectionContext context, CancellationToken ct = default);
}
