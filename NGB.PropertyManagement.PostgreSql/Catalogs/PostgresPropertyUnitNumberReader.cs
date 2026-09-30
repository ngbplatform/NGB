using Dapper;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Catalogs;

namespace NGB.PropertyManagement.PostgreSql.Catalogs;

internal sealed class PostgresPropertyUnitNumberReader(IUnitOfWork uow) : IPropertyUnitNumberReader
{
    public async Task<IReadOnlySet<string>> GetExistingAsync(
        Guid buildingId,
        IReadOnlyCollection<string> unitNumbers,
        CancellationToken ct = default)
    {
        if (unitNumbers.Count == 0)
            return new HashSet<string>(StringComparer.Ordinal);

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT p.unit_no
                             FROM cat_pm_property p
                             JOIN catalogs c ON c.id = p.catalog_id
                            WHERE p.kind = 'Unit'
                              AND p.parent_property_id = @buildingId
                              AND p.unit_no = ANY(@unitNumbers)
                              AND c.catalog_code = 'pm.property'
                              AND NOT c.is_deleted;
                           """;

        var rows = await uow.Connection.QueryAsync<string>(new CommandDefinition(
            sql,
            new
            {
                buildingId,
                unitNumbers = unitNumbers.Distinct(StringComparer.Ordinal).ToArray()
            },
            transaction: uow.Transaction,
            cancellationToken: ct));

        return rows.ToHashSet(StringComparer.Ordinal);
    }
}
