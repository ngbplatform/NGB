using NGB.Application.Abstractions.Services;
using NGB.Contracts.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Core.Security;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Security;
using NGB.Runtime.UnitOfWork;

namespace NGB.Runtime.Documents.Actions;

internal sealed class DocumentActionQueryService(
    DocumentService documents,
    IDocumentRepository documentRepository,
    IPermissionSnapshotProvider permissions,
    DocumentActionEvaluator evaluator,
    IUnitOfWork uow)
    : IDocumentActionQueryService
{
    public async Task<DocumentEditorStateDto> GetEditorStateAsync(
        string documentType,
        Guid documentId,
        CancellationToken ct)
    {
        var snapshot = await permissions.GetCurrentAsync(ct);
        var view = new NgbPermissionKey(NgbResourceKinds.Document, documentType, NgbPermissionActions.View);
        if (!snapshot.Has(view))
            throw new NgbPermissionDeniedException(view);

        return await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            var document = await documentRepository.GetAsync(documentId, innerCt)
                ?? throw new DocumentNotFoundException(documentId);

            if (!string.Equals(document.TypeCode, documentType, StringComparison.OrdinalIgnoreCase))
                throw new DocumentTypeMismatchException(documentId, documentType, document.TypeCode);

            var dto = await documents.GetByIdAsync(documentType, documentId, innerCt);
            var facts = await evaluator.LoadFactsAsync(document, dto, snapshot, innerCt);
            var actions = await evaluator.EvaluateAllAsync(document, dto, snapshot, facts, innerCt);

            return new DocumentEditorStateDto(
                dto,
                document.Version,
                actions.Select(static x => x.Dto).ToArray());
        }, ct);
    }
}
