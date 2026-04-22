using System.ComponentModel.DataAnnotations;

namespace NGB.Accounting.PostingState;

/// <summary>
/// Logical posting operation for idempotency control.
/// Values are persisted to DB (SMALLINT), so treat them as stable.
/// </summary>
public enum PostingOperation : short
{
    [Display(Name = "Post")]
    Post = 1,

    [Display(Name = "Unpost")]
    Unpost = 2,

    [Display(Name = "Repost")]
    Repost = 3,

    /// <summary>
    /// Fiscal year closing entries (Income/Expense -> Retained Earnings).
    /// </summary>
    [Display(Name = "Close fiscal year")]
    CloseFiscalYear = 4
}
