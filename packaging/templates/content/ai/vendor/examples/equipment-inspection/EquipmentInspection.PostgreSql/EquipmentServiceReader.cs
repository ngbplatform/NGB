using Dapper;
using EquipmentInspection.Definitions;
using NGB.Persistence.UnitOfWork;

namespace EquipmentInspection.PostgreSql;

public sealed class EquipmentServiceReader(IUnitOfWork unitOfWork) : IEquipmentServiceReader
{
    public async Task<EquipmentServiceDetails?> ReadAsync(Guid documentId, CancellationToken ct)
    {
        unitOfWork.EnsureActiveTransaction();

        return await unitOfWork.Connection.QuerySingleOrDefaultAsync<EquipmentServiceDetails>(new CommandDefinition(
            """
            SELECT equipment_id AS EquipmentId, hours AS Hours, amount AS Amount, condition AS Condition
            FROM doc_example_equipment_service WHERE document_id = @documentId
            """,
            new { documentId },
            unitOfWork.Transaction,
            cancellationToken: ct));
    }
}
