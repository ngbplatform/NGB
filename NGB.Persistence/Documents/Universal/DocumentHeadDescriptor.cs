using NGB.Metadata.Base;

namespace NGB.Persistence.Documents.Universal;

/// <summary>
/// Describes a document type head table (doc_*) for universal, metadata-driven CRUD.
///
/// Identifiers (table/column names) must come from trusted metadata (Definitions).
/// Implementations must quote identifiers defensively.
/// </summary>
public sealed record DocumentHeadDescriptor(
    string TypeCode,
    string HeadTableName,
    string DisplayColumn,
    IReadOnlyList<DocumentHeadColumn> Columns);

public sealed record DocumentHeadColumn(string ColumnName, ColumnType ColumnType);
