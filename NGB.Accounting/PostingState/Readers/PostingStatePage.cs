namespace NGB.Accounting.PostingState.Readers;

public sealed record PostingStatePage(
    IReadOnlyList<PostingStateRecord> Records,
    bool HasMore,
    PostingStateCursor? NextCursor);
