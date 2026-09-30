namespace NGB.Metadata.Schema;

public sealed record DbSchemaSnapshot(
    IReadOnlySet<string> Tables,
    IReadOnlyDictionary<string, IReadOnlyList<DbColumnSchema>> ColumnsByTable,
    IReadOnlyDictionary<string, IReadOnlyList<DbForeignKeySchema>> ForeignKeysByTable,
    IReadOnlyDictionary<string, IReadOnlyList<DbIndexSchema>> IndexesByTable
)
{
    /// <summary>
    /// Optional provider-specific database objects loaded with the same logical schema snapshot.
    /// Null means that the inspector does not support bulk object inspection and validators must
    /// use their provider fallback checks.
    /// </summary>
    public DbSchemaObjectSnapshot? DatabaseObjects { get; init; }
}

public sealed record DbSchemaObjectSnapshot(
    IReadOnlySet<string> FunctionNames,
    IReadOnlyList<DbTriggerSchema> Triggers,
    IReadOnlyList<DbConstraintSchema> Constraints);

public sealed record DbTriggerSchema(string TriggerName, string TableName);

public sealed record DbConstraintSchema(string ConstraintName, string TableName);

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
