using Dapper;
using EquipmentInspection.Definitions;
using NGB.Persistence.UnitOfWork;

namespace EquipmentInspection.PostgreSql;

public sealed class EquipmentConditionReader(IUnitOfWork unitOfWork) : IEquipmentConditionReader
{
    public async Task<EquipmentConditionDetails?> ReadAsync(Guid documentId, CancellationToken ct)
    {
        unitOfWork.EnsureActiveTransaction();

        return await unitOfWork.Connection.QuerySingleOrDefaultAsync<EquipmentConditionDetails>(new CommandDefinition(
            "SELECT equipment_id AS EquipmentId, condition AS Condition FROM doc_example_equipment_condition WHERE document_id = @documentId",
            new { documentId },
            unitOfWork.Transaction,
            cancellationToken: ct));
    }
}
