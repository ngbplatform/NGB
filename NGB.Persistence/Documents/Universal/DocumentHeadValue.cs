using NGB.Metadata.Base;

namespace NGB.Persistence.Documents.Universal;

public sealed record DocumentHeadValue(string ColumnName, ColumnType ColumnType, object? Value);
