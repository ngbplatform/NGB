namespace NGB.Runtime.OperationalRegisters.Projections;

/// <summary>
/// Adapter allowing legacy <see cref="IOperationalRegisterMonthFinalizer"/> implementations
/// to be used as projectors.
/// </summary>
internal sealed class LegacyFinalizerProjectorAdapter(IOperationalRegisterMonthFinalizer finalizer)
    : IOperationalRegisterMonthProjector
{
    public string RegisterCodeNorm => finalizer.RegisterCodeNorm;

    public Task RebuildMonthAsync(OperationalRegisterMonthProjectionContext context, CancellationToken ct = default)
        => finalizer.FinalizeMonthAsync(context.RegisterId, context.PeriodMonth, context.NowUtc, ct);
}
