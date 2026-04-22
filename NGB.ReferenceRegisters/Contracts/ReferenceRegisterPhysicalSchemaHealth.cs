namespace NGB.ReferenceRegisters.Contracts;

/// <summary>
/// Health check result for a single physical table.
/// </summary>
public sealed record ReferenceRegisterPhysicalTableHealth(
    string TableName,
    bool Exists,
    IReadOnlyList<string> MissingColumns,
    IReadOnlyList<string> MissingIndexes,
    bool? HasAppendOnlyGuard)
{
    public bool IsOk => Exists
        && MissingColumns.Count == 0
        && MissingIndexes.Count == 0
        && (HasAppendOnlyGuard is null || HasAppendOnlyGuard.Value);
}

/// <summary>
/// Physical schema health for one Reference Register.
/// </summary>
public sealed record ReferenceRegisterPhysicalSchemaHealth(
    ReferenceRegisterAdminItem Register,
    ReferenceRegisterPhysicalTableHealth Records)
{
    public bool IsOk => Records.IsOk;
}

/// <summary>
/// Report for all registers.
/// </summary>
public sealed record ReferenceRegisterPhysicalSchemaHealthReport(
    IReadOnlyList<ReferenceRegisterPhysicalSchemaHealth> Items)
{
    public int TotalCount => Items.Count;

    public int OkCount => Items.Count(x => x.IsOk);
}
