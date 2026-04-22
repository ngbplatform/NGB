namespace NGB.Accounting.Periods;

/// <summary>
/// Read model for closed periods (accounting month closing audit).
/// </summary>
public sealed class ClosedPeriodRecord
{
    public DateOnly Period { get; init; }
    public string ClosedBy { get; init; } = null!;
    public DateTime ClosedAtUtc { get; init; }
}
