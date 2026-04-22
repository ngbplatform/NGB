using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;

namespace NGB.Runtime.OperationalRegisters;

/// <summary>
/// Runtime facade over <see cref="IOperationalRegisterFinalizationRepository"/>.
///
/// Used by future rebuild/finalization pipelines to mark derived projections (turnovers/balances)
/// as Dirty/Finalized per (register_id, month).
/// </summary>
public interface IOperationalRegisterFinalizationService
{
    Task<OperationalRegisterFinalization?> GetAsync(
        Guid registerId,
        DateOnly period,
        CancellationToken ct = default);

    Task MarkDirtyAsync(
        Guid registerId,
        DateOnly period,
        bool manageTransaction = true,
        CancellationToken ct = default);

    Task MarkFinalizedAsync(
        Guid registerId,
        DateOnly period,
        bool manageTransaction = true,
        CancellationToken ct = default);
}
