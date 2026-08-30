using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Metadata.Documents.Storage;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents.Validation;
using NGB.Runtime.Documents.Numbering;
using NGB.Runtime.Documents.Policies;
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.Locks;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Documents;

/// <summary>
/// Draft-only application service.
/// - Creates a Draft row in the common document registry (documents).
/// - Coordinates per-type typed storage via <see cref="DocumentWriteEngine"/>.
/// This service intentionally does NOT perform posting.
/// </summary>
internal sealed class DocumentDraftService(
    IUnitOfWork uow,
    IAdvisoryLockManager advisoryLocks,
    IDocumentRepository documents,
    DocumentWriteEngine writeEngine,
    IDocumentValidatorResolver validators,
    IDocumentNumberingAndTypedSyncService numberingSync,
    IDocumentNumberingPolicyResolver numberingPolicies,
    IDocumentTypeRegistry documentTypes,
    IAuditLogService audit,
    TimeProvider timeProvider)
    : IDocumentDraftBatchService
{
    private const int MaxCreateBatchSize = 1_000;
    private const string OpUpdateDraft = "DocumentDraft.UpdateDraft";
    private const string OpDeleteDraft = "DocumentDraft.DeleteDraft";

    public async Task<Guid> CreateDraftAsync(
        string typeCode,
        string? number,
        DateTime dateUtc,
        bool manageTransaction = true,
        bool suppressAudit = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(typeCode))
            throw new NgbArgumentRequiredException(nameof(typeCode));

        dateUtc.EnsureUtc(nameof(dateUtc));

        if (documentTypes.TryGet(typeCode) is null)
            throw new DocumentTypeNotFoundException(typeCode);

        var normalizedNumber = string.IsNullOrWhiteSpace(number)
            ? null
            : number.Trim();

        var id = Guid.CreateVersion7();

        return await uow.ExecuteInUowTransactionAsync(
            manageTransaction,
            async innerCt =>
            {
                var now = timeProvider.GetUtcNowDateTime();
                await CreateInCurrentTransactionAsync(
                    id,
                    typeCode,
                    normalizedNumber,
                    dateUtc,
                    now,
                    suppressAudit,
                    acquireLock: true,
                    ct: innerCt);

                return id;
            },
            ct);
    }

    public async Task<IReadOnlyList<Guid>> CreateDraftsAsync(
        IReadOnlyList<DocumentDraftCreateRequest> requests,
        bool manageTransaction = true,
        bool suppressAudit = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        if (requests.Count == 0)
            return [];

        if (requests.Count > MaxCreateBatchSize)
        {
            throw new NgbArgumentOutOfRangeException(
                nameof(requests),
                requests.Count,
                $"At most {MaxCreateBatchSize} drafts are allowed per batch.");
        }

        var prepared = new PreparedDraft[requests.Count];
        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index]
                ?? throw new NgbArgumentInvalidException(nameof(requests), "Draft batch must not contain null items.");

            if (string.IsNullOrWhiteSpace(request.TypeCode))
                throw new NgbArgumentRequiredException(nameof(requests));

            request.DateUtc.EnsureUtc(nameof(requests));
            if (documentTypes.TryGet(request.TypeCode) is null)
                throw new DocumentTypeNotFoundException(request.TypeCode);

            prepared[index] = new PreparedDraft(
                Guid.CreateVersion7(),
                request.TypeCode,
                string.IsNullOrWhiteSpace(request.Number) ? null : request.Number.Trim(),
                request.DateUtc);
        }

        return await uow.ExecuteInUowTransactionAsync(manageTransaction, async innerCt =>
            {
                var now = timeProvider.GetUtcNowDateTime();
                var requiresIndividualNumbering = prepared
                    .Where(item => item.Number is null
                        && numberingPolicies.Resolve(item.TypeCode)?.EnsureNumberOnCreateDraft == true)
                    .ToArray();
                var individuallyNumberedIds = requiresIndividualNumbering
                    .Select(static item => item.Id)
                    .ToHashSet();
                var batchable = prepared
                    .Where(item => !individuallyNumberedIds.Contains(item.Id))
                    .ToArray();

                // Mixed batches must use one global lock order. Locking the batchable
                // subset first and auto-numbered drafts afterwards can otherwise invert
                // the order seen by another transaction and create a deadlock cycle.
                await advisoryLocks.LockDocumentsDeterministicallyAsync(
                    prepared.Select(static item => item.Id).ToArray(),
                    innerCt);

                if (batchable.Length > 0)
                {
                    await CreateBatchInCurrentTransactionAsync(
                        batchable,
                        now,
                        suppressAudit,
                        innerCt);
                }

                foreach (var item in requiresIndividualNumbering)
                {
                    await CreateInCurrentTransactionAsync(
                        item.Id,
                        item.TypeCode,
                        item.Number,
                        item.DateUtc,
                        now,
                        suppressAudit,
                        acquireLock: false,
                        ct: innerCt);
                }

                return (IReadOnlyList<Guid>)prepared.Select(static item => item.Id).ToArray();
            },
            ct);
    }

    private async Task CreateBatchInCurrentTransactionAsync(
        IReadOnlyList<PreparedDraft> prepared,
        DateTime now,
        bool suppressAudit,
        CancellationToken ct)
    {
        var records = prepared
            .Select(item => new DocumentRecord
            {
                Id = item.Id,
                TypeCode = item.TypeCode,
                Number = item.Number,
                DateUtc = item.DateUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            })
            .ToArray();

        foreach (var record in records)
        {
            foreach (var validator in validators.ResolveDraftValidators(record.TypeCode))
            {
                await validator.ValidateCreateDraftAsync(record, ct);
            }
        }

        if (documents is IDocumentDraftBatchRepository batchRepository)
        {
            await batchRepository.CreateDraftsAsync(records, ct);
        }
        else
        {
            foreach (var record in records)
            {
                await documents.CreateAsync(record, ct);
            }
        }

        foreach (var group in records.GroupBy(static record => record.TypeCode, StringComparer.OrdinalIgnoreCase))
        {
            await writeEngine.EnsureDraftStorageCreatedManyAsync(
                group.Select(static record => record.Id).ToArray(),
                group.Key,
                acquireLocks: false,
                ct);
        }

        if (suppressAudit)
            return;

        await audit.WriteBatchAsync(
            records.Select(record => new AuditLogWriteRequest(
                    AuditEntityKind.Document,
                    record.Id,
                    AuditActionCodes.DocumentCreateDraft,
                    BuildCreateDraftChanges(record.TypeCode, record.Number, record.DateUtc),
                    new
                    {
                        typeCode = record.TypeCode,
                        number = record.Number,
                        dateUtc = record.DateUtc
                    }))
                .ToArray(),
            ct);
    }

    public async Task<bool> UpdateDraftAsync(
        Guid documentId,
        string? number,
        DateTime? dateUtc,
        bool manageTransaction = true,
        CancellationToken ct = default)
    {
        documentId.EnsureRequired(nameof(documentId));
        if (dateUtc is not null)
            dateUtc.Value.EnsureUtc(nameof(dateUtc));

        // If the caller passes neither field, treat this as an explicit no-op.
        if (number is null && dateUtc is null)
            return false;

        return await uow.ExecuteInUowTransactionAsync(
            manageTransaction,
            async innerCt =>
            {
                await advisoryLocks.LockDocumentAsync(documentId, innerCt);

                var doc = await documents.GetForUpdateAsync(documentId, innerCt)
                          ?? throw new DocumentNotFoundException(documentId);

                if (doc.Status == DocumentStatus.MarkedForDeletion)
                {
                    throw new DocumentMarkedForDeletionException(
                        operation: OpUpdateDraft,
                        documentId: documentId,
                        markedForDeletionAtUtc: doc.MarkedForDeletionAtUtc ?? timeProvider.GetUtcNowDateTime());
                }

                if (doc.Status != DocumentStatus.Draft)
                {
                    throw new DocumentWorkflowStateMismatchException(
                        operation: OpUpdateDraft,
                        documentId: documentId,
                        expectedState: DocumentStatus.Draft.ToString(),
                        actualState: doc.Status.ToString());
                }

                var newNumber = number is null
                    ? doc.Number
                    : string.IsNullOrWhiteSpace(number)
                        ? null
                        : number.Trim();

                var newDateUtc = dateUtc ?? doc.DateUtc;

                var didNumberChange = newNumber != doc.Number;
                var didDateChange = newDateUtc != doc.DateUtc;

                if (!didNumberChange && !didDateChange)
                    return false;

                var now = timeProvider.GetUtcNowDateTime();

                // Re-use draft validators for updates: they enforce invariants that depend only on common document fields.
                var updatedRecord = new DocumentRecord
                {
                    Id = doc.Id,
                    TypeCode = doc.TypeCode,
                    Number = newNumber,
                    DateUtc = newDateUtc,
                    Status = doc.Status,
                    CreatedAtUtc = doc.CreatedAtUtc,
                    UpdatedAtUtc = now,
                    PostedAtUtc = doc.PostedAtUtc,
                    MarkedForDeletionAtUtc = doc.MarkedForDeletionAtUtc
                };

                foreach (var v in validators.ResolveDraftValidators(doc.TypeCode))
                {
                    await v.ValidateCreateDraftAsync(updatedRecord, innerCt);
                }

                await documents.UpdateDraftHeaderAsync(documentId, newNumber, newDateUtc, now, innerCt);

                // Allow a document type to keep common draft header fields in its typed tables.
                // This hook runs in the same transaction and MUST be safe to call multiple times.
                await writeEngine.UpdateDraftStorageAsync(updatedRecord, acquireLock: false, innerCt);

                var changes = new List<AuditFieldChange>();
                if (didNumberChange)
                    changes.Add(AuditLogService.Change("number", doc.Number, newNumber));
                
                if (didDateChange)
                    changes.Add(AuditLogService.Change("date_utc", doc.DateUtc, newDateUtc));
                
                changes.Add(AuditLogService.Change("updated_at_utc", doc.UpdatedAtUtc, now));

                await audit.WriteAsync(
                    entityKind: AuditEntityKind.Document,
                    entityId: documentId,
                    actionCode: AuditActionCodes.DocumentUpdateDraft,
                    changes: changes,
                    metadata: new
                    {
                        doc.TypeCode,
                        number = newNumber,
                        dateUtc = newDateUtc
                    },
                    ct: innerCt);

                return true;
            },
            ct);
    }

    public async Task<bool> DeleteDraftAsync(
        Guid documentId,
        bool manageTransaction = true,
        CancellationToken ct = default)
    {
        documentId.EnsureRequired(nameof(documentId));
        return await uow.ExecuteInUowTransactionAsync(
            manageTransaction,
            async innerCt =>
            {
                await advisoryLocks.LockDocumentAsync(documentId, innerCt);

                var doc = await documents.GetForUpdateAsync(documentId, innerCt);
                if (doc is null)
                    return false; // idempotent no-op

                // Draft deletion is allowed for Draft and MarkedForDeletion.
                // This is important because MarkForDeletion is a Draft-only state and should not become a dead end.
                if (doc.Status is not (DocumentStatus.Draft or DocumentStatus.MarkedForDeletion))
                {
                    throw new DocumentWorkflowStateMismatchException(
                        operation: OpDeleteDraft,
                        documentId: documentId,
                        expectedState: $"{DocumentStatus.Draft} or {DocumentStatus.MarkedForDeletion}",
                        actualState: doc.Status.ToString());
                }

                // Delete typed draft storage first (important when typed tables have FK RESTRICT/NO ACTION).
                await writeEngine.DeleteDraftStorageAsync(documentId, doc.TypeCode, acquireLock: false, innerCt);

                var deleted = await documents.TryDeleteAsync(documentId, innerCt);
                if (!deleted)
                    return false;

                var changes = new List<AuditFieldChange>
                {
                    AuditLogService.Change("type_code", doc.TypeCode, null),
                    AuditLogService.Change("date_utc", doc.DateUtc, null),
                    AuditLogService.Change("status", doc.Status, null)
                };

                if (!string.IsNullOrWhiteSpace(doc.Number))
                    changes.Add(AuditLogService.Change("number", doc.Number, null));

                await audit.WriteAsync(
                    entityKind: AuditEntityKind.Document,
                    entityId: documentId,
                    actionCode: AuditActionCodes.DocumentDeleteDraft,
                    changes: changes,
                    metadata: new
                    {
                        doc.TypeCode,
                        doc.Number,
                        doc.DateUtc
                    },
                    ct: innerCt);

                return true;
            },
            ct);
    }

    private async Task CreateInCurrentTransactionAsync(
        Guid id,
        string typeCode,
        string? number,
        DateTime dateUtc,
        DateTime nowUtc,
        bool suppressAudit,
        bool acquireLock,
        CancellationToken ct)
    {
        if (acquireLock)
            await advisoryLocks.LockDocumentAsync(id, ct);

        var numberingPolicy = numberingPolicies.Resolve(typeCode);

        var record = new DocumentRecord
        {
            Id = id,
            TypeCode = typeCode,
            Number = number,
            DateUtc = dateUtc,
            Status = DocumentStatus.Draft,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null
        };

        foreach (var v in validators.ResolveDraftValidators(typeCode))
        {
            await v.ValidateCreateDraftAsync(record, ct);
        }

        await documents.CreateAsync(record, ct);

        // Create typed draft storage if a module provides it.
        await writeEngine.EnsureDraftStorageCreatedAsync(id, typeCode, acquireLock: false, ct);

        string? assignedNumber = null;
        if (string.IsNullOrWhiteSpace(number) && numberingPolicy?.EnsureNumberOnCreateDraft == true)
        {
            var locked = await documents.GetForUpdateAsync(id, ct)
                         ?? throw new DocumentNotFoundException(id);

            // Assign number and keep typed draft storage in sync in the same transaction.
            assignedNumber = await numberingSync.EnsureNumberAndSyncTypedAsync(locked, nowUtc, ct);
        }

        // Audit: create draft
        var changes = new List<AuditFieldChange>
        {
            AuditLogService.Change("type_code", null, typeCode),
            AuditLogService.Change("date_utc", null, dateUtc),
            AuditLogService.Change("status", null, DocumentStatus.Draft)
        };

        var effectiveNumber = assignedNumber ?? number;
        if (!string.IsNullOrWhiteSpace(effectiveNumber))
            changes.Add(AuditLogService.Change("number", null, effectiveNumber));

        if (!suppressAudit)
        {
            await audit.WriteAsync(
                entityKind: AuditEntityKind.Document,
                entityId: id,
                actionCode: AuditActionCodes.DocumentCreateDraft,
                changes: changes,
                metadata: new
                {
                    typeCode,
                    number = effectiveNumber,
                    dateUtc
                },
                ct: ct);
        }
    }

    private static IReadOnlyList<AuditFieldChange> BuildCreateDraftChanges(
        string typeCode,
        string? number,
        DateTime dateUtc)
    {
        var changes = new List<AuditFieldChange>
        {
            AuditLogService.Change("type_code", null, typeCode),
            AuditLogService.Change("date_utc", null, dateUtc),
            AuditLogService.Change("status", null, DocumentStatus.Draft)
        };

        if (!string.IsNullOrWhiteSpace(number))
            changes.Add(AuditLogService.Change("number", null, number));

        return changes;
    }

    private sealed record PreparedDraft(Guid Id, string TypeCode, string? Number, DateTime DateUtc);
}
