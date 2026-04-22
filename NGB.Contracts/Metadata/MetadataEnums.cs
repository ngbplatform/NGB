namespace NGB.Contracts.Metadata;

public enum DataType
{
    String = 1,
    Int32 = 2,
    Decimal = 3,
    Money = 4,
    Boolean = 5,
    Date = 6,
    DateTime = 7,
    Guid = 8,
    Enum = 9,
    Lookup = 10,
    DimensionSet = 11
}

public enum UiControl
{
    Input = 1,
    TextArea = 2,
    Number = 3,
    Money = 4,
    Checkbox = 5,
    Date = 6,
    DateTime = 7,
    Select = 8,
    Lookup = 9,
    DimensionSet = 10
}

public enum EntityKind
{
    Catalog = 1,
    Document = 2,
    Report = 3,
    Admin = 4
}

public enum DocumentStatus
{
    Draft = 1,
    Posted = 2,
    MarkedForDeletion = 3
}
