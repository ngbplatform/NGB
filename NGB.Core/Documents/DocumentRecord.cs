using NGB.Core.Base;

namespace NGB.Core.Documents;

/// <summary>
/// Platform-level document header stored in the database.
/// Concrete business documents (SalesInvoice, Payment, etc.) live in solution modules and
/// have their own tables; this record contains only fields common to all documents.
/// </summary>
public sealed class DocumentRecord : Entity
{
    /// <summary>
    /// Document type code. Naming convention: "payment", "sales_invoice", etc.
    /// Used to bind to per-type tables: doc_{type_code}, doc_{type_code}__{part}, ...
    /// </summary>
    public required string TypeCode { get; init; }

    public string? Number { get; init; }

    /// <summary>
    /// Document date in UTC.
    /// </summary>
    public required DateTime DateUtc { get; init; }

    public required DocumentStatus Status { get; init; }

    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }

    public DateTime? PostedAtUtc { get; init; }
    public DateTime? MarkedForDeletionAtUtc { get; init; }
}
