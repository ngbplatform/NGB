namespace NGB.PropertyManagement.Contracts;

public static class FifoApplyLimits
{
    public const int DefaultMaxApplications = 50;
    public const int MaxApplications = 100;

    // Suggestions are read-only and can be larger. Execution posts every apply document
    // within one transaction, so it shares the platform atomic posting lock budget.
    public const int DefaultMaxAtomicApplications = 25;
    public const int MaxAtomicApplications = 25;
}
