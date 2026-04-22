using NGB.OperationalRegisters.Contracts;

namespace NGB.Runtime.OperationalRegisters;

/// <summary>
/// JSON-friendly DTO contracts for Operational Registers admin endpoints.
///
/// Notes:
/// - These DTOs intentionally mirror admin read models, but they are Runtime-owned
///   (stable surface for Web API / UI).
/// - Avoid exposing persistence types directly to keep endpoint payloads stable.
/// </summary>
public static class OperationalRegisterAdminEndpointContracts
{
    public sealed record RegisterDto(
        Guid RegisterId,
        string Code,
        string CodeNorm,
        string TableCode,
        string Name,
        bool HasMovements,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    public sealed record RegisterListItemDto(
        RegisterDto Register,
        int ResourcesCount,
        int DimensionRulesCount);

    public sealed record ResourceDto(
        string Code,
        string CodeNorm,
        string ColumnCode,
        string Name,
        int Ordinal);

    public sealed record DimensionRuleDto(
        Guid DimensionId,
        string DimensionCode,
        int Ordinal,
        bool IsRequired);

    public sealed record RegisterDetailsDto(
        RegisterDto Register,
        IReadOnlyList<ResourceDto> Resources,
        IReadOnlyList<DimensionRuleDto> DimensionRules);

    public sealed record PhysicalTableHealthDto(
        string TableName,
        bool Exists,
        IReadOnlyList<string> MissingColumns,
        IReadOnlyList<string> MissingIndexes,
        bool? HasAppendOnlyGuard,
        bool IsOk);

    public sealed record PhysicalSchemaHealthDto(
        RegisterDto Register,
        PhysicalTableHealthDto Movements,
        PhysicalTableHealthDto Turnovers,
        PhysicalTableHealthDto Balances,
        bool IsOk);

    public sealed record PhysicalSchemaHealthReportDto(
        IReadOnlyList<PhysicalSchemaHealthDto> Items,
        int TotalCount,
        int OkCount);

    public sealed record FinalizationDto(
        Guid RegisterId,
        DateOnly Period,
        string Status,
        DateTime? FinalizedAtUtc,
        DateTime? DirtySinceUtc,
        DateTime? BlockedSinceUtc,
        string? BlockedReason,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    internal static RegisterDto ToDto(this OperationalRegisterAdminItem x)
        => new(
            x.RegisterId,
            x.Code,
            x.CodeNorm,
            x.TableCode,
            x.Name,
            x.HasMovements,
            x.CreatedAtUtc,
            x.UpdatedAtUtc);

    internal static RegisterListItemDto ToDto(this OperationalRegisterAdminListItem x)
        => new(x.Register.ToDto(), x.ResourcesCount, x.DimensionRulesCount);

    internal static ResourceDto ToDto(this OperationalRegisterResource x)
        => new(x.Code, x.CodeNorm, x.ColumnCode, x.Name, x.Ordinal);

    internal static DimensionRuleDto ToDto(this OperationalRegisterDimensionRule x)
        => new(x.DimensionId, x.DimensionCode, x.Ordinal, x.IsRequired);

    internal static RegisterDetailsDto ToDto(this OperationalRegisterAdminDetails x)
        => new(
            x.Register.ToDto(),
            x.Resources.Select(r => r.ToDto()).ToArray(),
            x.DimensionRules.Select(r => r.ToDto()).ToArray());

    internal static PhysicalTableHealthDto ToDto(this OperationalRegisterPhysicalTableHealth x)
        => new(
            x.TableName,
            x.Exists,
            x.MissingColumns,
            x.MissingIndexes,
            x.HasAppendOnlyGuard,
            x.IsOk);

    internal static PhysicalSchemaHealthDto ToDto(this OperationalRegisterPhysicalSchemaHealth x)
        => new(
            x.Register.ToDto(),
            x.Movements.ToDto(),
            x.Turnovers.ToDto(),
            x.Balances.ToDto(),
            x.IsOk);

    internal static PhysicalSchemaHealthReportDto ToDto(this OperationalRegisterPhysicalSchemaHealthReport x)
        => new(
            x.Items.Select(i => i.ToDto()).ToArray(),
            x.TotalCount,
            x.OkCount);

    internal static FinalizationDto ToDto(this OperationalRegisterFinalization x)
        => new(
            x.RegisterId,
            x.Period,
            x.Status.ToString(),
            x.FinalizedAtUtc,
            x.DirtySinceUtc,
            x.BlockedSinceUtc,
            x.BlockedReason,
            x.CreatedAtUtc,
            x.UpdatedAtUtc);
}
