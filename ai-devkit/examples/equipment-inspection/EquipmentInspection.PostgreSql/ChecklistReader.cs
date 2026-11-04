using Dapper;
using EquipmentInspection.Definitions;
using NGB.Persistence.UnitOfWork;

namespace EquipmentInspection.PostgreSql;

public sealed class ChecklistReader(IUnitOfWork unitOfWork) : IChecklistReader
{
    public async Task<ChecklistSummary?> ReadAsync(Guid documentId, CancellationToken ct)
    {
        unitOfWork.EnsureActiveTransaction();

        return await unitOfWork.Connection.QuerySingleOrDefaultAsync<ChecklistSummary>(new CommandDefinition(
            """
            SELECT head.reviewed AS Reviewed,
                   COUNT(checks.line_no) AS LineCount,
                   COUNT(checks.line_no) FILTER (
                       WHERE NULLIF(BTRIM(checks.description), '') IS NULL OR checks.passed IS NULL
                   ) AS IncompleteCount
            FROM doc_example_checklist head
            LEFT JOIN doc_example_checklist_checks checks ON checks.document_id = head.document_id
            WHERE head.document_id = @documentId
            GROUP BY head.document_id, head.reviewed
            """,
            new { documentId },
            unitOfWork.Transaction,
            cancellationToken: ct));
    }
}
