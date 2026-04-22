namespace NGB.Metadata.Base;

/// <summary>
/// Provider-agnostic logical column types used by document typed storage metadata.
/// Concrete DB providers map these to physical column types (postgres, sqlserver, oracle, ...).
/// </summary>
public enum ColumnType
{
    String = 0,
    Int32 = 1,
    Int64 = 2,
    Decimal = 3,
    Boolean = 4,
    Guid = 5,
    Date = 6,
    DateTimeUtc = 7,
    Json = 8
}
