namespace NGB.Accounting.PostingState.Readers;

/// <summary>
/// Keyset cursor for paging posting log ordered by StartedAtUtc DESC, then DocumentId DESC, then Operation DESC.
/// </summary>
public sealed record PostingStateCursor(DateTime AfterStartedAtUtc, Guid AfterDocumentId, short AfterOperation);
