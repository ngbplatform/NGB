using NGB.Persistence.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;

namespace NGB.Runtime.ReferenceRegisters;

/// <summary>
/// Default implementation of <see cref="IReferenceRegisterAdminReadService"/>.
/// </summary>
internal sealed class ReferenceRegisterAdminReadService(
    IReferenceRegisterRepository registers,
    IReferenceRegisterFieldRepository fields,
    IReferenceRegisterDimensionRuleRepository dimensionRules,
    IReferenceRegisterPhysicalSchemaHealthReader schemaHealth)
    : IReferenceRegisterAdminReadService
{
    public async Task<IReadOnlyList<(ReferenceRegisterAdminItem Register, int FieldsCount, int DimensionRulesCount)>> GetListAsync(
        CancellationToken ct = default)
    {
        var list = await registers.GetAllAsync(ct);
        if (list.Count == 0)
            return [];

        var registerIds = list.Select(static register => register.RegisterId).ToArray();
        var fieldCounts = await fields.CountByRegisterIdsAsync(registerIds, ct);
        var dimensionRuleCounts = await dimensionRules.CountByRegisterIdsAsync(registerIds, ct);
        var result = new List<(ReferenceRegisterAdminItem, int, int)>(list.Count);

        foreach (var r in list)
        {
            result.Add((
                r,
                fieldCounts.GetValueOrDefault(r.RegisterId),
                dimensionRuleCounts.GetValueOrDefault(r.RegisterId)));
        }

        return result;
    }

    public async Task<ReferenceRegisterAdminDetails?> GetDetailsByIdAsync(Guid registerId, CancellationToken ct = default)
    {
        var reg = await registers.GetByIdAsync(registerId, ct);
        if (reg is null)
            return null;

        var f = await fields.GetByRegisterIdAsync(registerId, ct);
        var d = await dimensionRules.GetByRegisterIdAsync(registerId, ct);
        return new ReferenceRegisterAdminDetails(reg, f, d);
    }

    public async Task<ReferenceRegisterAdminDetails?> GetDetailsByCodeAsync(string code, CancellationToken ct = default)
    {
        var reg = await registers.GetByCodeAsync(code, ct);
        if (reg is null)
            return null;

        var f = await fields.GetByRegisterIdAsync(reg.RegisterId, ct);
        var d = await dimensionRules.GetByRegisterIdAsync(reg.RegisterId, ct);
        return new ReferenceRegisterAdminDetails(reg, f, d);
    }

    public Task<ReferenceRegisterPhysicalSchemaHealthReport> GetPhysicalSchemaHealthReportAsync(
        CancellationToken ct = default)
        => schemaHealth.GetReportAsync(ct);

    public Task<ReferenceRegisterPhysicalSchemaHealth?> GetPhysicalSchemaHealthByIdAsync(
        Guid registerId,
        CancellationToken ct = default)
        => schemaHealth.GetByRegisterIdAsync(registerId, ct);
}
