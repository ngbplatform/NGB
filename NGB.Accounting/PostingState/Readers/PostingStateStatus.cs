using System.ComponentModel.DataAnnotations;

namespace NGB.Accounting.PostingState.Readers;

public enum PostingStateStatus : byte
{
    [Display(Name = "In progress")]
    InProgress = 1,

    [Display(Name = "Completed")]
    Completed = 2,

    [Display(Name = "Stale in progress")]
    StaleInProgress = 3
}
