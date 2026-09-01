namespace NGB.OperationalRegisters.Contracts;

/// <summary>
/// Hard resource boundaries shared by runtime orchestration and persistence.
/// Callers should repeat bounded runs instead of requesting an unbounded in-memory batch.
/// </summary>
public static class OperationalRegisterFinalizationLimits
{
    public const int DefaultProcessingBatchSize = 50;
    public const int MaxProcessingBatchSize = 500;
    public const int DefaultReadPageSize = 100;
    public const int MaxReadPageSize = 1_000;
}
