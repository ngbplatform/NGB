using Dapper;
using NGB.CRM.Seeding;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.CRM.PostgreSql.Seeding;

public sealed class PostgresCrmDemoSeedStateReader(IUnitOfWork uow) : ICrmDemoSeedStateReader
{
    public async Task<int> CountLeadIntakesByNamePrefixAsync(string leadNamePrefix, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(leadNamePrefix))
            throw new NgbArgumentRequiredException(nameof(leadNamePrefix));

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT COUNT(*)::int
                           FROM doc_crm_lead_intake
                           WHERE lead_name LIKE @Prefix;
                           """;

        return await uow.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new { Prefix = leadNamePrefix.Trim() + "%" },
            transaction: uow.Transaction,
            cancellationToken: ct));
    }
}
