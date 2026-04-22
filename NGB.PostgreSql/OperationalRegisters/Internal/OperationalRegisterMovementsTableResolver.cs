using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.OperationalRegisters;

namespace NGB.PostgreSql.OperationalRegisters.Internal;

internal static class OperationalRegisterMovementsTableResolver
{
    public static async Task<(string TableName, IReadOnlyList<string> ResourceColumns)> ResolveOrThrowAsync(
        IOperationalRegisterRepository registers,
        IOperationalRegisterResourceRepository resources,
        Guid registerId,
        CancellationToken ct)
    {
        var reg = await registers.GetByIdAsync(registerId, ct);
        if (reg is null)
            throw new OperationalRegisterNotFoundException(registerId);

        var tableName = OperationalRegisterNaming.MovementsTable(reg.TableCode);
        OperationalRegisterSqlIdentifiers.EnsureOrThrow(tableName, "opreg movements table name");

        var cols = (await resources.GetByRegisterIdAsync(registerId, ct))
            .OrderBy(r => r.Ordinal)
            .Select(r => r.ColumnCode)
            .ToArray();

        foreach (var c in cols)
        {
            OperationalRegisterSqlIdentifiers.EnsureOrThrow(c, "opreg resource column_code");
        }

        return (tableName, cols);
    }
}
