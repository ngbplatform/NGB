namespace NGB.Contracts.BusinessObjects;

/// <summary>Supported business object identities. Persisted values must not be reordered.</summary>
public enum BusinessObjectKind
{
    CatalogItem = 1,
    Document = 2,
    GeneralJournalEntry = 3
}

/// <summary>Provider-neutral identity; type code is the canonical platform definition code.</summary>
public readonly record struct BusinessObjectRef(BusinessObjectKind Kind, string TypeCode, Guid Id);

/// <summary>A bounded page ordered by immutable resource ID descending.</summary>
public sealed record BusinessObjectPage<T>(IReadOnlyList<T> Items, Guid? NextCursor);

/// <summary>Counts are null when the caller has no read permission for that capability.</summary>
public sealed record BusinessObjectContentSummary(long? Attachments, long? Notes);
