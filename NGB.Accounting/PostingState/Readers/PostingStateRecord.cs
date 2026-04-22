namespace NGB.Accounting.PostingState.Readers;

public sealed record PostingStateRecord(
    Guid DocumentId,
    PostingOperation Operation,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    PostingStateStatus Status,
    TimeSpan? Duration,
    TimeSpan Age);
