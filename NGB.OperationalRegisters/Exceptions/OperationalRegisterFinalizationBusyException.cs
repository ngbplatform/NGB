using NGB.Tools.Exceptions;

namespace NGB.OperationalRegisters.Exceptions;

/// <summary>A bounded publication attempt could not complete; the transaction must be rolled back.</summary>
public sealed class OperationalRegisterFinalizationBusyException(Guid registerId, DateOnly period, Exception inner)
    : NgbInfrastructureException(
        "Operational register finalization could not publish within its deadline.",
        "opreg.finalization.busy",
        new Dictionary<string, object?>
        {
            ["registerId"] = registerId,
            ["period"] = period
        },
        inner);
