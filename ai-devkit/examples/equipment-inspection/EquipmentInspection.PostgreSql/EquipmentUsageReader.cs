using Dapper;
using EquipmentInspection.Definitions;
using NGB.Persistence.UnitOfWork;

namespace EquipmentInspection.PostgreSql;

public sealed class EquipmentUsageReader(IUnitOfWork unitOfWork) : IEquipmentUsageReader
{
    public async Task<EquipmentUsageDetails?> ReadAsync(Guid documentId, CancellationToken ct)
    {
        unitOfWork.EnsureActiveTransaction();

        return await unitOfWork.Connection.QuerySingleOrDefaultAsync<EquipmentUsageDetails>(new CommandDefinition(
            "SELECT equipment_id AS EquipmentId, hours AS Hours FROM doc_example_equipment_usage WHERE document_id = @documentId",
            new { documentId },
            unitOfWork.Transaction,
            cancellationToken: ct));
    }
}
