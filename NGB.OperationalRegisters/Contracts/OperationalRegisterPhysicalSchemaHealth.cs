namespace NGB.OperationalRegisters.Contracts;

/// <summary>
/// Health check result for a single physical table.
/// </summary>
public sealed record OperationalRegisterPhysicalTableHealth(
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
/// Physical schema health for one Operational Register.
/// </summary>
public sealed record OperationalRegisterPhysicalSchemaHealth(
    OperationalRegisterAdminItem Register,
    OperationalRegisterPhysicalTableHealth Movements,
    OperationalRegisterPhysicalTableHealth Turnovers,
    OperationalRegisterPhysicalTableHealth Balances)
{
    public bool IsOk => Movements.IsOk && Turnovers.IsOk && Balances.IsOk;
}

/// <summary>
/// Report for all registers.
/// </summary>
public sealed record OperationalRegisterPhysicalSchemaHealthReport(
    IReadOnlyList<OperationalRegisterPhysicalSchemaHealth> Items)
{
    public int TotalCount => Items.Count;

    public int OkCount => Items.Count(x => x.IsOk);
}
