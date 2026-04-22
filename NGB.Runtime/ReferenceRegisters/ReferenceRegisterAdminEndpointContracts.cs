using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;

namespace NGB.Runtime.ReferenceRegisters;

/// <summary>
/// JSON-friendly DTO contracts for Reference Registers admin endpoints.
///
/// Notes:
/// - These DTOs intentionally mirror admin read models, but they are Runtime-owned
///   (stable surface for Web API / UI).
/// - Avoid exposing persistence types directly to keep endpoint payloads stable.
/// </summary>
public static class ReferenceRegisterAdminEndpointContracts
{
    public sealed record RegisterDto(
        Guid RegisterId,
        string Code,
        string CodeNorm,
        string TableCode,
        string Name,
        ReferenceRegisterPeriodicity Periodicity,
        ReferenceRegisterRecordMode RecordMode,
        bool HasRecords,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    public sealed record RegisterListItemDto(
        RegisterDto Register,
        int FieldsCount,
        int DimensionRulesCount);

    public sealed record FieldDto(
        string Code,
        string CodeNorm,
        string ColumnCode,
        string Name,
        int Ordinal,
        Metadata.Base.ColumnType ColumnType,
        bool IsNullable);

    public sealed record DimensionRuleDto(
        Guid DimensionId,
        string DimensionCode,
        int Ordinal,
        bool IsRequired);

    public sealed record RegisterDetailsDto(
        RegisterDto Register,
        IReadOnlyList<FieldDto> Fields,
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
        PhysicalTableHealthDto Records,
        bool IsOk);

    public sealed record PhysicalSchemaHealthReportDto(
        IReadOnlyList<PhysicalSchemaHealthDto> Items,
        int TotalCount,
        int OkCount);

    internal static RegisterDto ToDto(this ReferenceRegisterAdminItem x)
        => new(
            x.RegisterId,
            x.Code,
            x.CodeNorm,
            x.TableCode,
            x.Name,
            x.Periodicity,
            x.RecordMode,
            x.HasRecords,
            x.CreatedAtUtc,
            x.UpdatedAtUtc);

    internal static FieldDto ToDto(this ReferenceRegisterField x)
        => new(
            x.Code,
            x.CodeNorm,
            x.ColumnCode,
            x.Name,
            x.Ordinal,
            x.ColumnType,
            x.IsNullable);

    internal static DimensionRuleDto ToDto(this ReferenceRegisterDimensionRule x)
        => new(x.DimensionId, x.DimensionCode, x.Ordinal, x.IsRequired);

    internal static RegisterDetailsDto ToDto(this ReferenceRegisterAdminDetails x)
        => new(
            x.Register.ToDto(),
            x.Fields.Select(f => f.ToDto()).ToArray(),
            x.DimensionRules.Select(r => r.ToDto()).ToArray());

    internal static PhysicalTableHealthDto ToDto(this ReferenceRegisterPhysicalTableHealth x)
        => new(
            x.TableName,
            x.Exists,
            x.MissingColumns,
            x.MissingIndexes,
            x.HasAppendOnlyGuard,
            x.IsOk);

    internal static PhysicalSchemaHealthDto ToDto(this ReferenceRegisterPhysicalSchemaHealth x)
        => new(
            x.Register.ToDto(),
            x.Records.ToDto(),
            x.IsOk);

    internal static PhysicalSchemaHealthReportDto ToDto(this ReferenceRegisterPhysicalSchemaHealthReport x)
        => new(
            x.Items.Select(i => i.ToDto()).ToArray(),
            x.TotalCount,
            x.OkCount);
}
