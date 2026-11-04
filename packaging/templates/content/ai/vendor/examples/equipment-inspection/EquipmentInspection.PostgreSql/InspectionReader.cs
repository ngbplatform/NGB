using Dapper;
using EquipmentInspection.Definitions;
using NGB.Persistence.UnitOfWork;

namespace EquipmentInspection.PostgreSql;

public sealed class InspectionReader(IUnitOfWork unitOfWork) : IInspectionReader
{
    public async Task<InspectionDetails?> ReadAsync(Guid documentId, CancellationToken ct)
    {
        unitOfWork.EnsureActiveTransaction();

        return await unitOfWork.Connection.QuerySingleOrDefaultAsync<InspectionDetails>(new CommandDefinition(
            "SELECT equipment_id AS EquipmentId, result AS Result FROM doc_example_inspection WHERE document_id = @documentId",
            new { documentId },
            unitOfWork.Transaction,
            cancellationToken: ct));
    }
}
