namespace NGB.Accounting.PostingState;

/// <summary>
/// Result of trying to begin a posting operation for (document_id, operation).
/// </summary>
public enum PostingStateBeginResult : short
{
    Begun = 1,
    AlreadyCompleted = 2,
    InProgress = 3
}
