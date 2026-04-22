using Dapper;
using NGB.Persistence.Documents.Universal;

namespace NGB.PostgreSql.Documents;

public interface IPostgresDocumentListFilterSqlContributor
{
    bool TryBuildClause(
        DocumentHeadDescriptor head,
        DocumentFilter filter,
        string documentAlias,
        string headAlias,
        string parameterName,
        DynamicParameters parameters,
        out string clause);
}
