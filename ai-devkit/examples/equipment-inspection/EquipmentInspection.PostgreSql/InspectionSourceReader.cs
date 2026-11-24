using Dapper;
using EquipmentInspection.Definitions;
using NGB.Core.Documents;
using NGB.Persistence.UnitOfWork;

namespace EquipmentInspection.PostgreSql;

public sealed class InspectionSourceReader(IUnitOfWork unitOfWork) : IInspectionSourceReader
{
    // The lookup and calculation use exactly the same eligibility predicate.
    // A draft does not consume its source; the first posted reinspection does.
    private const string Eligible = """
        FROM documents source
        JOIN doc_example_inspection inspection ON inspection.document_id = source.id
        WHERE source.type_code = @TypeCode
          AND source.status = @Posted
          AND source.marked_for_deletion_at_utc IS NULL
          AND inspection.equipment_id = @EquipmentId
          AND NOT EXISTS (
              SELECT 1 FROM document_relationships relationship
              JOIN documents target ON target.id = relationship.from_document_id
              WHERE relationship.to_document_id = source.id
                AND relationship.relationship_code_norm = 'example.reinspection_of'
                AND target.id <> @CurrentId
                AND target.status = @Posted
          )
        """;

    public async Task<IReadOnlyList<InspectionSource>> SearchAsync(
        Guid equipmentId, string query, int offset, int limit, CancellationToken ct)
    {
        await unitOfWork.EnsureConnectionOpenAsync(ct);
        var rows = await unitOfWork.Connection.QueryAsync<InspectionSource>(new CommandDefinition(
            """
            SELECT source.id AS Id, source.number AS Number,
                   inspection.equipment_id AS EquipmentId, inspection.result AS Result
            """ + "\n" + Eligible + """

              AND strpos(lower(source.number), lower(@Query)) > 0
            ORDER BY source.date_utc DESC, source.id
            OFFSET @Offset LIMIT @Limit
            """,
            new
            {
                TypeCode = EquipmentDefinitions.InspectionCode,
                Posted = (short)DocumentStatus.Posted,
                EquipmentId = equipmentId,
                CurrentId = Guid.Empty,
                Query = query,
                Offset = offset,
                Limit = limit
            },
            unitOfWork.Transaction,
            cancellationToken: ct));
        return rows.ToArray();
    }

    public async Task<InspectionSource?> ReadEligibleAsync(Guid id, Guid equipmentId, CancellationToken ct)
    {
        await unitOfWork.EnsureConnectionOpenAsync(ct);
        return await unitOfWork.Connection.QuerySingleOrDefaultAsync<InspectionSource>(new CommandDefinition(
            """
            SELECT source.id AS Id, source.number AS Number,
                   inspection.equipment_id AS EquipmentId, inspection.result AS Result
            """ + "\n" + Eligible + " AND source.id = @Id",
            new
            {
                TypeCode = EquipmentDefinitions.InspectionCode,
                Posted = (short)DocumentStatus.Posted,
                EquipmentId = equipmentId,
                CurrentId = Guid.Empty,
                Id = id
            },
            unitOfWork.Transaction,
            cancellationToken: ct));
    }

    public async Task<bool> CanPostReinspectionAsync(Guid documentId, CancellationToken ct)
    {
        unitOfWork.EnsureActiveTransaction();
        var sourceId = await unitOfWork.Connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            """
            SELECT to_document_id FROM document_relationships
            WHERE from_document_id = @DocumentId AND relationship_code_norm = 'example.reinspection_of'
            """,
            new { DocumentId = documentId },
            unitOfWork.Transaction,
            cancellationToken: ct));
        if (sourceId is null)
            return true;

        // Serialize competing confirmations before taking a fresh eligibility snapshot.
        await unitOfWork.Connection.ExecuteAsync(new CommandDefinition(
            "SELECT id FROM documents WHERE id = @Id FOR UPDATE", new { Id = sourceId },
            unitOfWork.Transaction, cancellationToken: ct));
        var equipmentId = await unitOfWork.Connection.QuerySingleAsync<Guid?>(new CommandDefinition(
            "SELECT equipment_id FROM doc_example_inspection WHERE document_id = @Id", new { Id = documentId },
            unitOfWork.Transaction, cancellationToken: ct));
        return await unitOfWork.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 " + Eligible + """

              AND source.id = @Id
              AND source.date_utc <= (SELECT date_utc FROM documents WHERE id = @CurrentId))
            """,
            new
            {
                TypeCode = EquipmentDefinitions.InspectionCode,
                Posted = (short)DocumentStatus.Posted,
                EquipmentId = equipmentId,
                CurrentId = documentId,
                Id = sourceId
            },
            unitOfWork.Transaction,
            cancellationToken: ct));
    }
}
