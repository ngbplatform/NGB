namespace NGB.Runtime.OperationalRegisters.Projections;

/// <summary>
/// Default projector used when no register-specific projector is registered.
///
/// It provides a production-safe baseline so operational register finalization works out of the box,
/// while modules can still override it with a custom <see cref="IOperationalRegisterMonthProjector"/>.
/// </summary>
public interface IOperationalRegisterDefaultMonthProjector
{
    Task RebuildMonthAsync(OperationalRegisterMonthProjectionContext context, CancellationToken ct = default);
}
