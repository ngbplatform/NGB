namespace NGB.Core.Locks;

/// <summary>
/// Period advisory lock scopes.
///
/// Period locks are month-level and intentionally coarse. Different subsystems may legitimately
/// operate on the same calendar month concurrently without corrupting each other.
///
/// We therefore namespace period locks by subsystem scope.
/// </summary>
public enum AdvisoryLockPeriodScope
{
    /// <summary>
    /// Accounting subsystem (posting, unposting, reposting, period close, rebuild).
    /// </summary>
    Accounting = 1,

    /// <summary>
    /// Operational Registers subsystem (movement writes, month finalization).
    /// </summary>
    OperationalRegister = 2,
}
