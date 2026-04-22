namespace NGB.OperationalRegisters.Contracts;

/// <summary>
/// Operational register finalization status.
/// Values are persisted to DB (SMALLINT), so treat them as stable.
///
/// Table: operational_register_finalizations
/// </summary>
public enum OperationalRegisterFinalizationStatus : short
{
    /// <summary>
    /// Projections for the month are consistent and accepted.
    /// </summary>
    Finalized = 1,

    /// <summary>
    /// There are backdated movements and projections must be rebuilt.
    /// </summary>
    Dirty = 2,

    /// <summary>
    /// Finalization is blocked because no projector is registered for this register.
    /// The runner will not retry blocked months until they are explicitly marked Dirty again.
    /// </summary>
    BlockedNoProjector = 3
}
