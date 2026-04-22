namespace NGB.Metadata.Schema;

public sealed record DbSchemaSnapshot(
    IReadOnlySet<string> Tables,
    IReadOnlyDictionary<string, IReadOnlyList<DbColumnSchema>> ColumnsByTable,
    IReadOnlyDictionary<string, IReadOnlyList<DbForeignKeySchema>> ForeignKeysByTable,
    IReadOnlyDictionary<string, IReadOnlyList<DbIndexSchema>> IndexesByTable
);

public sealed record DbColumnSchema(
    string TableName,
    string ColumnName,
    string DbType,
    bool IsNullable,
    int? CharacterMaximumLength
);

public sealed record DbForeignKeySchema(
    string TableName,
    string ConstraintName,
    string ColumnName,
    string ReferencedTableName,
    string ReferencedColumnName
);

public sealed record DbIndexSchema(
    string TableName,
    string IndexName,
    IReadOnlyList<string> ColumnNames,
    bool IsUnique
);
