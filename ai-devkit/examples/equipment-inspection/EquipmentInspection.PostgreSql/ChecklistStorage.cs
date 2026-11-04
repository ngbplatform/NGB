using Dapper;
using EquipmentInspection.Definitions;
using NGB.Persistence.Documents.Storage;
using NGB.Persistence.UnitOfWork;

namespace EquipmentInspection.PostgreSql;

public sealed class ChecklistStorage(IUnitOfWork unitOfWork) : IDocumentTypeStorage
{
    public string TypeCode => ChecklistDefinitions.DocumentCode;

    public async Task CreateDraftAsync(Guid documentId, CancellationToken ct = default)
    {
        unitOfWork.EnsureActiveTransaction();

        await unitOfWork.Connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO doc_example_checklist(document_id) VALUES (@documentId) ON CONFLICT (document_id) DO NOTHING",
            new { documentId },
            unitOfWork.Transaction,
            cancellationToken: ct));
    }

    public async Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default)
    {
        unitOfWork.EnsureActiveTransaction();

        // The application migration owns the cascade from the head to its check rows.
        await unitOfWork.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM doc_example_checklist WHERE document_id = @documentId",
            new { documentId },
            unitOfWork.Transaction,
            cancellationToken: ct));
    }
}
