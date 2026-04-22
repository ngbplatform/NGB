using Microsoft.Extensions.Logging;
using NGB.Accounting.Posting;
using NGB.Accounting.PostingState;
using NGB.Accounting.Registers;
using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.Readers;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Runtime.Documents.Workflow;
using NGB.Tools.Exceptions;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Posting;
using NGB.Runtime.Diagnostics;
using NGB.Runtime.Documents.Posting;
using NGB.Runtime.Documents.Validation;
using NGB.Runtime.Documents.Numbering;
using NGB.Runtime.Documents.Policies;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.ReferenceRegisters;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Documents;

/// <summary>
/// Application service that orchestrates document lifecycle transitions with accounting posting.
///
/// Hybrid storage model:
/// - The common document registry is stored in the <c>documents</c> table.
/// - Each document type stores its unique header fields and table parts in its own tables
///   (doc_document_type, doc_document_type__parts).
///
/// This service manages only the registry + posting invariants. It does NOT know about the
/// per-type tables; vertical solutions handle them.
///
/// Atomicity:
/// - Document status changes MUST be committed in the same DB transaction as accounting posting.
/// - Therefore <see cref="PostingEngine"/> is called with manageTransaction=false.
/// </summary>
internal sealed class DocumentPostingService(
    IUnitOfWork uow,
    IAdvisoryLockManager advisoryLocks,
    IDocumentRepository documents,
    PostingEngine postingEngine,
    IAccountingPostingContextFactory accountingContextFactory,
    IAccountingEntryReader entryReader,
    DocumentPostingLifecycleCoordinator lifecycleCoordinator,
    IDocumentPostingActionResolver postingActionResolver,
    IDocumentOperationalRegisterPostingActionResolver opregPostingActionResolver,
    IOperationalRegisterMovementsApplier opregMovementsApplier,
    IOperationalRegisterWriteStateRepository opregWriteStateRepository,
    IDocumentReferenceRegisterPostingActionResolver refregPostingActionResolver,
    IReferenceRegisterRecordsStore refregRecordsStore,
    IReferenceRegisterRecordsReader refregRecordsReader,
    IReferenceRegisterWriteStateRepository refregWriteStateRepository,
    IReferenceRegisterRepository refregRepository,
    IDocumentValidatorResolver validators,
    DocumentWriteEngine writeEngine,
    IDocumentNumberingAndTypedSyncService numberingSync,
    IDocumentNumberingPolicyResolver numberingPolicies,
    IAuditLogService audit,
    ILogger<DocumentPostingService> logger,
    TimeProvider timeProvider)
    : IDocumentPostingService
{
    /// <summary>
    /// Posts a Draft document.
    /// Preferred overload: <paramref name="postingAction"/> receives CancellationToken.
    /// </summary>
    public async Task PostAsync(
        Guid documentId,
        Func<IAccountingPostingContext, CancellationToken, Task> postingAction,
        CancellationToken ct = default)
    {
        if (postingAction is null)
            throw new NgbArgumentRequiredException(nameof(postingAction));

        await PostInternalAsync(documentId, postingAction, manageTransaction: true, ct);
    }

    public Task PostAsync(Guid documentId, CancellationToken ct = default)
        => PostInternalAsync(documentId, postingAction: null, manageTransaction: true, ct);

    public Task PostAsync(Guid documentId, bool manageTransaction, CancellationToken ct = default)
        => PostInternalAsync(documentId, postingAction: null, manageTransaction, ct);

    private async Task PostInternalAsync(
        Guid documentId,
        Func<IAccountingPostingContext, CancellationToken, Task>? postingAction,
        bool manageTransaction,
        CancellationToken ct)
    {
        var didWork = false;
        try
        {
            await uow.ExecuteInUowTransactionAsync(manageTransaction, async innerCt =>
            {
                await advisoryLocks.LockDocumentAsync(documentId, innerCt);

                var doc = await documents.GetForUpdateAsync(documentId, innerCt)
                          ?? throw new DocumentNotFoundException(documentId);

                var oldStatus = doc.Status;
                var oldPostedAt = doc.PostedAtUtc;
                var oldMarkedForDeletionAt = doc.MarkedForDeletionAtUtc;
                var oldUpdatedAt = doc.UpdatedAtUtc;
                var oldNumber = doc.Number;

                var period = new DateOnly(doc.DateUtc.Year, doc.DateUtc.Month, 1);
                using var scope = logger.BeginScope(new Dictionary<string, object?>
                {
                    ["DocumentId"] = documentId,
                    ["Operation"] = "Post",
                    ["Period"] = period.ToString("yyyy-MM-dd")
                });
                RuntimeLog.DocumentOperationStarted(logger, "Post");

                if (doc.Status == DocumentStatus.MarkedForDeletion)
                    throw new DocumentMarkedForDeletionException("Document.Post", documentId, doc.MarkedForDeletionAtUtc ?? doc.UpdatedAtUtc);

                if (doc.Status == DocumentStatus.Posted)
                {
                    RuntimeLog.DocumentOperationNoOp(logger, "Post");
                    return; // idempotent no-op
                }

                var numberingPolicy = numberingPolicies.Resolve(doc.TypeCode);
                if (numberingPolicy?.EnsureNumberOnPost == true && string.IsNullOrWhiteSpace(doc.Number))
                {
                    var nowForNumber = timeProvider.GetUtcNowDateTime();
                    await numberingSync.EnsureNumberAndSyncTypedAsync(doc, nowForNumber, innerCt);

                    // Re-read: numbering updates the DB, and validators / posting action may depend on the assigned number.
                    doc = await documents.GetForUpdateAsync(documentId, innerCt)
                          ?? throw new DocumentNotFoundException(documentId);
                }

                foreach (var v in validators.ResolvePostValidators(doc.TypeCode))
                {
                    await v.ValidateBeforePostAsync(doc, innerCt);
                }

                var hasOpreg = opregPostingActionResolver.TryResolve(doc) is not null;
                var hasRefreg = refregPostingActionResolver.TryResolve(doc) is not null;
                var accountingPostingAction = postingAction ?? postingActionResolver.TryResolve(doc);

                if (accountingPostingAction is null && !hasOpreg && !hasRefreg)
                    throw new DocumentPostingHandlerNotConfiguredException(doc.Id, doc.TypeCode);

                _ = await lifecycleCoordinator.BeginAsync(documentId, PostingOperation.Post, innerCt);

                if (accountingPostingAction is not null)
                {
                    async Task<PostingResult> ExecuteAccountingPostAsync()
                        => await postingEngine.PostAsync(
                            PostingOperation.Post,
                            accountingPostingAction,
                            manageTransaction: false,
                            innerCt);

                    await lifecycleCoordinator.ExecuteAccountingAsync(
                        documentId,
                        PostingOperation.Post,
                        ExecuteAccountingPostAsync,
                        innerCt);
                }

                await ApplyOperationalRegisterMovementsForPostAsync(doc, manageTransaction: false, innerCt);
                await ApplyReferenceRegisterRecordsForPostAsync(doc, manageTransaction: false, innerCt);

                var now = timeProvider.GetUtcNowDateTime();
                await documents.UpdateStatusAsync(
                    documentId,
                    DocumentStatus.Posted,
                    updatedAtUtc: now,
                    postedAtUtc: now,
                    markedForDeletionAtUtc: null,
                    innerCt);

                await lifecycleCoordinator.CompleteSuccessfulTransitionAsync(documentId, PostingOperation.Post, innerCt);

                // Audit: document.post (no-op is handled by early returns)
                var postChanges = new List<AuditFieldChange>
                {
                    AuditLogService.Change("status", oldStatus, DocumentStatus.Posted),
                    AuditLogService.Change("posted_at_utc", oldPostedAt, now),
                    AuditLogService.Change("updated_at_utc", oldUpdatedAt, now)
                };

                if (!string.Equals(oldNumber, doc.Number, StringComparison.Ordinal))
                    postChanges.Add(AuditLogService.Change("number", oldNumber, doc.Number));

                if (oldMarkedForDeletionAt is not null)
                    postChanges.Add(AuditLogService.Change("marked_for_deletion_at_utc", oldMarkedForDeletionAt, null));

                await audit.WriteAsync(
                    entityKind: AuditEntityKind.Document,
                    entityId: documentId,
                    actionCode: AuditActionCodes.DocumentPost,
                    changes: postChanges,
                    metadata: new
                    {
                        doc.TypeCode,
                        doc.Number,
                        doc.DateUtc
                    },
                    ct: innerCt);

                didWork = true;
            }, ct);

            if (didWork)
                RuntimeLog.DocumentOperationCompleted(logger, "Post");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Post failed.");
            throw;
        }
    }

    /// <summary>
    /// Unposts a Posted document by generating storno entries from the document's existing entries.
    /// After successful unposting, document returns to Draft.
    /// </summary>
    public async Task UnpostAsync(Guid documentId, CancellationToken ct = default)
    {
        var didWork = false;
        try
        {
            await uow.ExecuteInUowTransactionAsync(async innerCt =>
            {
                await advisoryLocks.LockDocumentAsync(documentId, innerCt);

                var doc = await documents.GetForUpdateAsync(documentId, innerCt)
                          ?? throw new DocumentNotFoundException(documentId);

                var oldStatus = doc.Status;
                var oldPostedAt = doc.PostedAtUtc;
                var oldMarkedForDeletionAt = doc.MarkedForDeletionAtUtc;
                var oldUpdatedAt = doc.UpdatedAtUtc;

                var period = new DateOnly(doc.DateUtc.Year, doc.DateUtc.Month, 1);
                using var scope = logger.BeginScope(new Dictionary<string, object?>
                {
                    ["DocumentId"] = documentId,
                    ["Operation"] = "Unpost",
                    ["Period"] = period.ToString("yyyy-MM-dd")
                });
                RuntimeLog.DocumentOperationStarted(logger, "Unpost");

                if (doc.Status == DocumentStatus.Draft)
                {
                    RuntimeLog.DocumentOperationNoOp(logger, "Unpost");
                    return; // idempotent no-op
                }

                if (doc.Status != DocumentStatus.Posted)
                    throw new DocumentWorkflowStateMismatchException("Document.Unpost", documentId, expectedState: "Posted", actualState: doc.Status.ToString());

                var oldEntries = await entryReader.GetByDocumentAsync(documentId, innerCt);
                if (oldEntries.Count == 0)
                {
                    // Register-only documents (no accounting postings) are allowed to be unposted.
                    // Still keep the invariant for accounting-backed documents.
                    var hasAccountingHandler = postingActionResolver.TryResolve(doc) is not null;
                    if (hasAccountingHandler)
                        throw new NgbInvariantViolationException("Document is Posted but has no entries.", new Dictionary<string, object?>
                        {
                            ["documentId"] = documentId,
                            ["typeCode"] = doc.TypeCode,
                            ["operation"] = "Document.Unpost"
                        });
                }

                _ = await lifecycleCoordinator.BeginAsync(documentId, PostingOperation.Unpost, innerCt);

                if (oldEntries.Count > 0)
                {
                    var entriesToReverse = await GetAccountingEntriesToReverseAsync(
                        doc,
                        oldEntries,
                        operation: "Document.Unpost",
                        ct: innerCt);
                    var stornoEntries = AccountingStornoFactory.Create(entriesToReverse);

                    async Task<PostingResult> ExecuteAccountingUnpostAsync()
                        => await postingEngine.PostAsync(
                            PostingOperation.Unpost,
                            (ctx, _) =>
                            {
                                foreach (var s in stornoEntries)
                                {
                                    ctx.Post(
                                        documentId: s.DocumentId,
                                        period: s.Period,
                                        debit: s.Debit,
                                        credit: s.Credit,
                                        amount: s.Amount,
                                        debitDimensions: s.DebitDimensions,
                                        creditDimensions: s.CreditDimensions,
                                        isStorno: true);

                                    var created = ctx.Entries[^1];
                                    created.DebitDimensionSetId = s.DebitDimensionSetId;
                                    created.CreditDimensionSetId = s.CreditDimensionSetId;
                                }

                                return Task.CompletedTask;
                            },
                            manageTransaction: false,
                            innerCt);

                    await lifecycleCoordinator.ExecuteAccountingAsync(
                        documentId,
                        PostingOperation.Unpost,
                        ExecuteAccountingUnpostAsync,
                        innerCt);
                }

                await ApplyOperationalRegisterMovementsForUnpostAsync(documentId, manageTransaction: false, innerCt);
                await ApplyReferenceRegisterRecordsForUnpostAsync(doc, manageTransaction: false, innerCt);

                var now = timeProvider.GetUtcNowDateTime();
                await documents.UpdateStatusAsync(
                    documentId,
                    DocumentStatus.Draft,
                    updatedAtUtc: now,
                    postedAtUtc: null,
                    markedForDeletionAtUtc: null,
                    innerCt);

                await lifecycleCoordinator.CompleteSuccessfulTransitionAsync(documentId, PostingOperation.Unpost, innerCt);

                // Audit: document.unpost (no-op is handled by early returns)
                var unpostChanges = new List<AuditFieldChange>
                {
                    AuditLogService.Change("status", oldStatus, DocumentStatus.Draft),
                    AuditLogService.Change("posted_at_utc", oldPostedAt, null),
                    AuditLogService.Change("updated_at_utc", oldUpdatedAt, now)
                };
                
                if (oldMarkedForDeletionAt is not null)
                    unpostChanges.Add(AuditLogService.Change("marked_for_deletion_at_utc", oldMarkedForDeletionAt, null));

                await audit.WriteAsync(
                    entityKind: AuditEntityKind.Document,
                    entityId: documentId,
                    actionCode: AuditActionCodes.DocumentUnpost,
                    changes: unpostChanges,
                    metadata: new
                    {
                        doc.TypeCode,
                        doc.Number,
                        doc.DateUtc
                    },
                    ct: innerCt);

                didWork = true;
            }, ct);

            if (didWork)
                RuntimeLog.DocumentOperationCompleted(logger, "Unpost");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unpost failed.");
            throw;
        }
    }

    /// <summary>
    /// Reposts a Posted document.
    /// Preferred overload: <paramref name="postNew"/> receives CancellationToken.
    /// </summary>
    public async Task RepostAsync(
        Guid documentId,
        Func<IAccountingPostingContext, CancellationToken, Task> postNew,
        CancellationToken ct = default)
    {
        if (postNew is null)
            throw new NgbArgumentRequiredException(nameof(postNew));

        var didWork = false;
        try
        {
            await uow.ExecuteInUowTransactionAsync(async innerCt =>
            {
                await advisoryLocks.LockDocumentAsync(documentId, innerCt);

                var doc = await documents.GetForUpdateAsync(documentId, innerCt)
                          ?? throw new DocumentNotFoundException(documentId);

                var oldPostedAt = doc.PostedAtUtc;
                var oldUpdatedAt = doc.UpdatedAtUtc;

                var period = new DateOnly(doc.DateUtc.Year, doc.DateUtc.Month, 1);
                using var scope = logger.BeginScope(new Dictionary<string, object?>
                {
                    ["DocumentId"] = documentId,
                    ["Operation"] = "Repost",
                    ["Period"] = period.ToString("yyyy-MM-dd")
                });
                
                RuntimeLog.DocumentOperationStarted(logger, "Repost");

                if (doc.Status != DocumentStatus.Posted)
                    throw new DocumentWorkflowStateMismatchException("Document.Repost", documentId, expectedState: "Posted", actualState: doc.Status.ToString());

                var oldEntries = await entryReader.GetByDocumentAsync(documentId, innerCt);
                if (oldEntries.Count == 0)
                {
                    // Register-only documents (no accounting postings) can be reposted without touching the posting engine.
                    var hasAccountingHandler = postingActionResolver.TryResolve(doc) is not null;
                    if (hasAccountingHandler)
                        throw new NgbInvariantViolationException("Document is Posted but has no entries.", new Dictionary<string, object?>
                        {
                            ["documentId"] = documentId,
                            ["typeCode"] = doc.TypeCode,
                            ["operation"] = "Document.Repost"
                        });
                }

                var repostBegin = await lifecycleCoordinator.BeginAsync(documentId, PostingOperation.Repost, innerCt);
                if (repostBegin == DocumentLifecycleBeginResult.NoOp)
                {
                    RuntimeLog.DocumentOperationNoOp(logger, "Repost");
                    return; // strict no-op
                }

                if (oldEntries.Count > 0)
                {
                    var entriesToReverse = await GetAccountingEntriesToReverseAsync(
                        doc,
                        oldEntries,
                        operation: "Document.Repost",
                        ct: innerCt);
                    var stornoEntries = AccountingStornoFactory.Create(entriesToReverse);

                    async Task<PostingResult> ExecuteAccountingRepostAsync()
                        => await postingEngine.PostAsync(
                            PostingOperation.Repost,
                            async (ctx, actionCt) =>
                            {
                                foreach (var s in stornoEntries)
                                {
                                    ctx.Post(
                                        documentId: s.DocumentId,
                                        period: s.Period,
                                        debit: s.Debit,
                                        credit: s.Credit,
                                        amount: s.Amount,
                                        debitDimensions: s.DebitDimensions,
                                        creditDimensions: s.CreditDimensions,
                                        isStorno: true);

                                    var created = ctx.Entries[^1];
                                    created.DebitDimensionSetId = s.DebitDimensionSetId;
                                    created.CreditDimensionSetId = s.CreditDimensionSetId;
                                }

                                await postNew(ctx, actionCt);
                            },
                            manageTransaction: false,
                            innerCt);

                    await lifecycleCoordinator.ExecuteAccountingAsync(
                        documentId,
                        PostingOperation.Repost,
                        ExecuteAccountingRepostAsync,
                        innerCt);
                }

                var opregDidWork = await ApplyOperationalRegisterMovementsForRepostAsync(doc, manageTransaction: false, innerCt);
                var refregDidWork = await ApplyReferenceRegisterRecordsForRepostAsync(doc, manageTransaction: false, innerCt);
                
                if (oldEntries.Count == 0 && !opregDidWork && !refregDidWork)
                {
                    await lifecycleCoordinator.CancelAsync(documentId, PostingOperation.Repost, innerCt);
                    RuntimeLog.DocumentOperationNoOp(logger, "Repost");
                    return; // strict no-op
                }

                var now = timeProvider.GetUtcNowDateTime();
                await documents.UpdateStatusAsync(
                    documentId,
                    DocumentStatus.Posted,
                    updatedAtUtc: now,
                    postedAtUtc: now,
                    markedForDeletionAtUtc: null,
                    innerCt);

                await lifecycleCoordinator.CompleteSuccessfulTransitionAsync(documentId, PostingOperation.Repost, innerCt);

                // Audit: document.repost (no-op is handled by early returns)
                var repostChanges = new List<AuditFieldChange>
                {
                    AuditLogService.Change("posted_at_utc", oldPostedAt, now),
                    AuditLogService.Change("updated_at_utc", oldUpdatedAt, now)
                };

                await audit.WriteAsync(
                    entityKind: AuditEntityKind.Document,
                    entityId: documentId,
                    actionCode: AuditActionCodes.DocumentRepost,
                    changes: repostChanges,
                    metadata: new
                    {
                        doc.TypeCode,
                        doc.Number,
                        doc.DateUtc
                    },
                    ct: innerCt);

                didWork = true;
            }, ct);

            if (didWork)
                RuntimeLog.DocumentOperationCompleted(logger, "Repost");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Repost failed.");
            throw;
        }
    }

    /// <summary>
    /// Safe default: only Draft documents can be marked for deletion.
    /// (For Posted documents you typically need an explicit workflow: Unpost then mark.)
    /// </summary>
    public async Task MarkForDeletionAsync(Guid documentId, CancellationToken ct = default)
    {
        var didWork = false;
        try
        {
            await uow.ExecuteInUowTransactionAsync(async innerCt =>
            {
                await advisoryLocks.LockDocumentAsync(documentId, innerCt);

                var doc = await documents.GetForUpdateAsync(documentId, innerCt)
                          ?? throw new DocumentNotFoundException(documentId);

                var oldStatus = doc.Status;
                var oldMarkedForDeletionAt = doc.MarkedForDeletionAtUtc;
                var oldUpdatedAt = doc.UpdatedAtUtc;

                var period = new DateOnly(doc.DateUtc.Year, doc.DateUtc.Month, 1);
                using var scope = logger.BeginScope(new Dictionary<string, object?>
                {
                    ["DocumentId"] = documentId,
                    ["Operation"] = "MarkForDeletion",
                    ["Period"] = period.ToString("yyyy-MM-dd")
                });
                RuntimeLog.DocumentOperationStarted(logger, "MarkForDeletion");

                if (doc.Status == DocumentStatus.Posted)
                    throw new DocumentWorkflowStateMismatchException("Document.MarkForDeletion", documentId, expectedState: "Draft", actualState: "Posted");

                if (doc.Status == DocumentStatus.MarkedForDeletion)
                {
                    RuntimeLog.DocumentOperationNoOp(logger, "MarkForDeletion");
                    return; // idempotent no-op
                }

                // Safe default: Draft-only deletion marking (future-proof if new statuses appear).
                if (doc.Status != DocumentStatus.Draft)
                    throw new DocumentWorkflowStateMismatchException("Document.MarkForDeletion", documentId, expectedState: "Draft", actualState: doc.Status.ToString());

                var now = timeProvider.GetUtcNowDateTime();
                await documents.UpdateStatusAsync(
                    documentId,
                    DocumentStatus.MarkedForDeletion,
                    updatedAtUtc: now,
                    postedAtUtc: null,
                    markedForDeletionAtUtc: now,
                    innerCt);

                var updatedDraft = await documents.GetForUpdateAsync(documentId, innerCt)
                                  ?? throw new DocumentNotFoundException(documentId);

                await writeEngine.UpdateDraftStorageAsync(updatedDraft, acquireLock: false, innerCt);

                // Audit: document.mark_for_deletion (no-op is handled by early returns)
                var markChanges = new List<AuditFieldChange>
                {
                    AuditLogService.Change("status", oldStatus, DocumentStatus.MarkedForDeletion),
                    AuditLogService.Change("marked_for_deletion_at_utc", oldMarkedForDeletionAt, now),
                    AuditLogService.Change("updated_at_utc", oldUpdatedAt, now)
                };

                await audit.WriteAsync(
                    entityKind: AuditEntityKind.Document,
                    entityId: documentId,
                    actionCode: AuditActionCodes.DocumentMarkForDeletion,
                    changes: markChanges,
                    metadata: new
                    {
                        doc.TypeCode,
                        doc.Number,
                        doc.DateUtc
                    },
                    ct: innerCt);

                didWork = true;
            }, ct);

            if (didWork)
                RuntimeLog.DocumentOperationCompleted(logger, "MarkForDeletion");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MarkForDeletion failed.");
            throw;
        }
    }

    /// <summary>
    /// Removes the deletion mark from a Draft document.
    /// Intended for the "oops" path when a user marked a draft for deletion by mistake.
    /// </summary>
    public async Task UnmarkForDeletionAsync(Guid documentId, CancellationToken ct = default)
    {
        var didWork = false;
        try
        {
            await uow.ExecuteInUowTransactionAsync(async innerCt =>
            {
                await advisoryLocks.LockDocumentAsync(documentId, innerCt);

                var doc = await documents.GetForUpdateAsync(documentId, innerCt)
                          ?? throw new DocumentNotFoundException(documentId);

                var oldStatus = doc.Status;
                var oldMarkedForDeletionAt = doc.MarkedForDeletionAtUtc;
                var oldUpdatedAt = doc.UpdatedAtUtc;

                var period = new DateOnly(doc.DateUtc.Year, doc.DateUtc.Month, 1);
                using var scope = logger.BeginScope(new Dictionary<string, object?>
                {
                    ["DocumentId"] = documentId,
                    ["Operation"] = "UnmarkForDeletion",
                    ["Period"] = period.ToString("yyyy-MM-dd")
                });
                RuntimeLog.DocumentOperationStarted(logger, "UnmarkForDeletion");

                if (doc.Status == DocumentStatus.Posted)
                    throw new DocumentWorkflowStateMismatchException("Document.UnmarkForDeletion", documentId, expectedState: "MarkedForDeletion", actualState: "Posted");

                if (doc.Status == DocumentStatus.Draft)
                {
                    RuntimeLog.DocumentOperationNoOp(logger, "UnmarkForDeletion");
                    return; // idempotent no-op
                }

                if (doc.Status != DocumentStatus.MarkedForDeletion)
                    throw new DocumentWorkflowStateMismatchException("Document.UnmarkForDeletion", documentId, expectedState: "MarkedForDeletion", actualState: doc.Status.ToString());

                var now = timeProvider.GetUtcNowDateTime();
                await documents.UpdateStatusAsync(
                    documentId,
                    DocumentStatus.Draft,
                    updatedAtUtc: now,
                    postedAtUtc: null,
                    markedForDeletionAtUtc: null,
                    innerCt);

                var updatedDraft = await documents.GetForUpdateAsync(documentId, innerCt)
                                  ?? throw new DocumentNotFoundException(documentId);

                await writeEngine.UpdateDraftStorageAsync(updatedDraft, acquireLock: false, innerCt);

                // Audit: document.unmark_for_deletion
                var changes = new List<AuditFieldChange>
                {
                    AuditLogService.Change("status", oldStatus, DocumentStatus.Draft),
                    AuditLogService.Change("marked_for_deletion_at_utc", oldMarkedForDeletionAt, null),
                    AuditLogService.Change("updated_at_utc", oldUpdatedAt, now)
                };

                await audit.WriteAsync(
                    entityKind: AuditEntityKind.Document,
                    entityId: documentId,
                    actionCode: AuditActionCodes.DocumentUnmarkForDeletion,
                    changes: changes,
                    metadata: new
                    {
                        doc.TypeCode,
                        doc.Number,
                        doc.DateUtc
                    },
                    ct: innerCt);

                didWork = true;
            }, ct);

            if (didWork)
                RuntimeLog.DocumentOperationCompleted(logger, "UnmarkForDeletion");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UnmarkForDeletion failed.");
            throw;
        }
    }

    private async Task ApplyOperationalRegisterMovementsForPostAsync(
        DocumentRecord doc,
        bool manageTransaction,
        CancellationToken ct)
    {
        var opregAction = opregPostingActionResolver.TryResolve(doc);
        if (opregAction is null)
            return;

        var builder = new OperationalRegisterMovementsBuilder(doc.Id);
        await opregAction(builder, ct);

        foreach (var (registerId, movements) in builder.MovementsByRegister.OrderBy(x => x.Key))
        {
            if (movements.Count == 0)
                continue;

            var result = await opregMovementsApplier.ApplyMovementsForDocumentAsync(
                registerId,
                doc.Id,
                OperationalRegisterWriteOperation.Post,
                movements,
                affectedPeriods: null,
                manageTransaction: manageTransaction,
                ct: ct);

            EnsureOperationalRegisterExecuted(result, registerId, doc.Id, OperationalRegisterWriteOperation.Post);
        }
    }

    private async Task ApplyOperationalRegisterMovementsForUnpostAsync(
        Guid documentId,
        bool manageTransaction,
        CancellationToken ct)
    {
        var registerIds = await opregWriteStateRepository.GetRegisterIdsByDocumentAsync(documentId, ct);
        if (registerIds.Count == 0)
            return;

        IReadOnlyList<OperationalRegisterMovement> empty = [];
        foreach (var registerId in registerIds.OrderBy(x => x))
        {
            var result = await opregMovementsApplier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Unpost,
                empty,
                affectedPeriods: null,
                manageTransaction: manageTransaction,
                ct: ct);

            EnsureOperationalRegisterExecuted(result, registerId, documentId, OperationalRegisterWriteOperation.Unpost);
        }
    }

    private async Task<bool> ApplyOperationalRegisterMovementsForRepostAsync(
        DocumentRecord doc,
        bool manageTransaction,
        CancellationToken ct)
    {
        var oldRegisterIds = await opregWriteStateRepository.GetRegisterIdsByDocumentAsync(doc.Id, ct);

        IReadOnlyDictionary<Guid, IReadOnlyList<OperationalRegisterMovement>> newMovementsByRegister =
            new Dictionary<Guid, IReadOnlyList<OperationalRegisterMovement>>();

        var opregAction = opregPostingActionResolver.TryResolve(doc);
        if (opregAction is not null)
        {
            var builder = new OperationalRegisterMovementsBuilder(doc.Id);
            await opregAction(builder, ct);
            newMovementsByRegister = builder.MovementsByRegister;
        }

        var registerIds = new HashSet<Guid>(oldRegisterIds);
        foreach (var id in newMovementsByRegister.Keys)
        {
            registerIds.Add(id);
        }

        if (registerIds.Count == 0)
            return false;

        var didWork = false;
        IReadOnlyList<OperationalRegisterMovement> empty = [];
        
        foreach (var registerId in registerIds.OrderBy(x => x))
        {
            var movements = newMovementsByRegister.GetValueOrDefault(registerId, empty);
            var result = await opregMovementsApplier.ApplyMovementsForDocumentAsync(
                registerId,
                doc.Id,
                OperationalRegisterWriteOperation.Repost,
                movements,
                affectedPeriods: null,
                manageTransaction: manageTransaction,
                ct: ct);

            if (result == OperationalRegisterWriteResult.AlreadyCompleted)
                throw BuildSubsystemStateConflict(doc.Id, "operational-register", registerId, nameof(OperationalRegisterWriteOperation.Repost));

            if (result == OperationalRegisterWriteResult.Executed)
                didWork = true;
        }

        return didWork;
    }

    private async Task ApplyReferenceRegisterRecordsForPostAsync(
        DocumentRecord doc,
        bool manageTransaction,
        CancellationToken ct)
    {
        var rrAction = refregPostingActionResolver.TryResolve(doc);
        if (rrAction is null)
            return;

        await uow.ExecuteInUowTransactionAsync(manageTransaction, async innerCt =>
        {
            var builder = new ReferenceRegisterRecordsBuilder(doc.Id);
            await rrAction(builder, ReferenceRegisterWriteOperation.Post, innerCt);

            var startedAtUtc = timeProvider.GetUtcNowDateTime();
            foreach (var (registerId, records) in builder.RecordsByRegister)
            {
                var begin = await refregWriteStateRepository.TryBeginAsync(
                    registerId,
                    doc.Id,
                    ReferenceRegisterWriteOperation.Post,
                    startedAtUtc,
                    innerCt);

                if (begin == PostingStateBeginResult.AlreadyCompleted)
                    throw BuildSubsystemStateConflict(doc.Id, "reference-register", registerId, nameof(ReferenceRegisterWriteOperation.Post));

                if (begin == PostingStateBeginResult.InProgress)
                {
                    throw new ReferenceRegisterWriteAlreadyInProgressException(registerId, doc.Id, nameof(ReferenceRegisterWriteOperation.Post));
                }

                await refregRecordsStore.AppendAsync(registerId, records, innerCt);
                await refregWriteStateRepository.MarkCompletedAsync(
                    registerId,
                    doc.Id,
                    ReferenceRegisterWriteOperation.Post,
                    timeProvider.GetUtcNowDateTime(),
                    innerCt);
            }
        }, ct);
    }

    private async Task ApplyReferenceRegisterRecordsForUnpostAsync(
        DocumentRecord doc,
        bool manageTransaction,
        CancellationToken ct)
    {
        await uow.ExecuteInUowTransactionAsync(manageTransaction, async innerCt =>
        {
            var registerIds = await refregWriteStateRepository.GetRegisterIdsByDocumentAsync(doc.Id, innerCt);
            if (registerIds.Count == 0)
                return;

            // For registers in Independent mode, the platform MUST NOT auto-delete ("storno") keys on Unpost.
            // Instead, we allow the per-document posting handler to emit append-only compensation records
            // (including tombstones) for Unpost, if the business workflow requires it.
            IReadOnlyDictionary<Guid, IReadOnlyList<ReferenceRegisterRecordWrite>> unpostRecordsByRegister =
                new Dictionary<Guid, IReadOnlyList<ReferenceRegisterRecordWrite>>();

            var rrAction = refregPostingActionResolver.TryResolve(doc);
            if (rrAction is not null)
            {
                var builder = new ReferenceRegisterRecordsBuilder(doc.Id);
                await rrAction(builder, ReferenceRegisterWriteOperation.Unpost, innerCt);
                unpostRecordsByRegister = builder.RecordsByRegister;
            }

            var recordModeCache = new Dictionary<Guid, ReferenceRegisterRecordMode>(capacity: registerIds.Count);

            var startedAtUtc = timeProvider.GetUtcNowDateTime();
            foreach (var registerId in registerIds)
            {
                var begin = await refregWriteStateRepository.TryBeginAsync(
                    registerId,
                    doc.Id,
                    ReferenceRegisterWriteOperation.Unpost,
                    startedAtUtc,
                    innerCt);

                if (begin == PostingStateBeginResult.AlreadyCompleted)
                    throw BuildSubsystemStateConflict(doc.Id, "reference-register", registerId, nameof(ReferenceRegisterWriteOperation.Unpost));

                if (begin == PostingStateBeginResult.InProgress)
                    throw new ReferenceRegisterWriteAlreadyInProgressException(registerId, doc.Id, nameof(ReferenceRegisterWriteOperation.Unpost));

                if (!recordModeCache.TryGetValue(registerId, out var recordMode))
                {
                    var reg = await refregRepository.GetByIdAsync(registerId, innerCt)
                              ?? throw new ReferenceRegisterNotFoundException(registerId);
                    recordMode = reg.RecordMode;
                    recordModeCache.Add(registerId, recordMode);
                }

                if (recordMode == ReferenceRegisterRecordMode.SubordinateToRecorder)
                {
                    // SubordinateToRecorder semantics: Unpost writes tombstones for all currently active keys produced by the recorder.
                    // Prefer store-side tombstones for performance and correctness (covers future-period rows too).
                    // Fallback to paging reader if the current persistence implementation doesn't support it.
                    if (refregRecordsStore is IReferenceRegisterRecorderTombstoneWriter tombstoneWriter)
                    {
                        await tombstoneWriter.AppendTombstonesForRecorderAsync(registerId, doc.Id, keepDimensionSetIds: null, innerCt);
                    }
                    else
                    {
                        var tombstones = await BuildReferenceRegisterRecorderTombstonesAsync(registerId, doc.Id, startedAtUtc, keepDimensionSetIds: null, innerCt);
                        if (tombstones.Count > 0)
                            await refregRecordsStore.AppendAsync(registerId, tombstones, innerCt);
                    }
                }
                else
                {
                    // Independent semantics: the platform does not auto-delete keys.
                    // If the handler emits records for Unpost, append them (still append-only).
                    if (unpostRecordsByRegister.TryGetValue(registerId, out var records) && records.Count > 0)
                        await refregRecordsStore.AppendAsync(registerId, records, innerCt);
                }

                await refregWriteStateRepository.MarkCompletedAsync(
                    registerId,
                    doc.Id,
                    ReferenceRegisterWriteOperation.Unpost,
                    timeProvider.GetUtcNowDateTime(),
                    innerCt);
            }
        }, ct);
    }

    private async Task<bool> ApplyReferenceRegisterRecordsForRepostAsync(
        DocumentRecord doc,
        bool manageTransaction,
        CancellationToken ct)
    {
        var didWork = false;

        await uow.ExecuteInUowTransactionAsync(manageTransaction, async innerCt =>
        {
            var oldRegisterIds = await refregWriteStateRepository.GetRegisterIdsByDocumentAsync(doc.Id, innerCt);

            IReadOnlyDictionary<Guid, IReadOnlyList<ReferenceRegisterRecordWrite>> newRecordsByRegister =
                new Dictionary<Guid, IReadOnlyList<ReferenceRegisterRecordWrite>>();

            var rrAction = refregPostingActionResolver.TryResolve(doc);
            if (rrAction is not null)
            {
                var builder = new ReferenceRegisterRecordsBuilder(doc.Id);
                await rrAction(builder, ReferenceRegisterWriteOperation.Repost, innerCt);
                newRecordsByRegister = builder.RecordsByRegister;
            }

            var registerIds = new HashSet<Guid>(oldRegisterIds);
            foreach (var id in newRecordsByRegister.Keys)
                registerIds.Add(id);

            if (registerIds.Count == 0)
                return;

            IReadOnlyList<ReferenceRegisterRecordWrite> empty = [];
            var startedAtUtc = timeProvider.GetUtcNowDateTime();

            foreach (var registerId in registerIds)
            {
                var records = newRecordsByRegister.GetValueOrDefault(registerId, empty);

                var begin = await refregWriteStateRepository.TryBeginAsync(
                    registerId,
                    doc.Id,
                    ReferenceRegisterWriteOperation.Repost,
                    startedAtUtc,
                    innerCt);

                if (begin == PostingStateBeginResult.AlreadyCompleted)
                    throw BuildSubsystemStateConflict(doc.Id, "reference-register", registerId, nameof(ReferenceRegisterWriteOperation.Repost));

                if (begin == PostingStateBeginResult.InProgress)
                    throw new ReferenceRegisterWriteAlreadyInProgressException(registerId, doc.Id, nameof(ReferenceRegisterWriteOperation.Repost));

                didWork = true;
                // SubordinateToRecorder semantics: Repost = tombstone removed keys (storno) + append new.
                // For Independent registers, tombstones (if needed) must be emitted by the handler itself.
                if (oldRegisterIds.Contains(registerId))
                {
                    var reg = await refregRepository.GetByIdAsync(registerId, innerCt)
                              ?? throw new ReferenceRegisterNotFoundException(registerId);

                    if (reg.RecordMode == ReferenceRegisterRecordMode.SubordinateToRecorder)
                    {
                        IReadOnlyCollection<Guid>? keepDimensionSetIds = null;

                        // Periodic nuance:
                        // The effective key includes PeriodUtc (and PeriodBucketUtc), but the current tombstone writer keep filter
                        // accepts only DimensionSetId. Therefore, on Repost for periodic registers we must be tombstone ALL
                        // recorder-produced effective versions (by period) and then append the new versions.
                        if (reg.Periodicity == ReferenceRegisterPeriodicity.NonPeriodic)
                        {
                            keepDimensionSetIds = records.Count == 0
                                ? []
                                : records
                                    .Where(r => !r.IsDeleted)
                                    .Select(r => r.DimensionSetId)
                                    .Distinct()
                                    .ToArray();
                        }

                        if (refregRecordsStore is IReferenceRegisterRecorderTombstoneWriter tombstoneWriter)
                        {
                            await tombstoneWriter.AppendTombstonesForRecorderAsync(registerId, doc.Id, keepDimensionSetIds, innerCt);
                        }
                        else
                        {
                            var tombstones = await BuildReferenceRegisterRecorderTombstonesAsync(
                                registerId,
                                doc.Id,
                                startedAtUtc,
                                keepDimensionSetIds: keepDimensionSetIds,
                                innerCt);

                            if (tombstones.Count > 0)
                                await refregRecordsStore.AppendAsync(registerId, tombstones, innerCt);
                        }
                    }
                }

                await refregRecordsStore.AppendAsync(registerId, records, innerCt);

                await refregWriteStateRepository.MarkCompletedAsync(
                    registerId,
                    doc.Id,
                    ReferenceRegisterWriteOperation.Repost,
                    timeProvider.GetUtcNowDateTime(),
                    innerCt);
            }
        }, ct);

        return didWork;
    }

    private async Task<IReadOnlyList<ReferenceRegisterRecordWrite>> BuildReferenceRegisterRecorderTombstonesAsync(
        Guid registerId,
        Guid recorderDocumentId,
        DateTime asOfUtc,
        IReadOnlyCollection<Guid>? keepDimensionSetIds,
        CancellationToken ct)
    {
        // We page through SliceLastAll because it provides the last version per key as-of moment,
        // including the field values needed to satisfy NOT NULL constraints.
        HashSet<Guid>? keep = null;
        if (keepDimensionSetIds is { Count: > 0 })
            keep = keepDimensionSetIds as HashSet<Guid> ?? [..keepDimensionSetIds];
        
        var list = new List<ReferenceRegisterRecordWrite>();
        Guid? after = null;

        while (true)
        {
            var page = await refregRecordsReader.SliceLastAllAsync(
                registerId,
                asOfUtc,
                recorderDocumentId,
                afterDimensionSetId: after,
                limit: 200,
                ct: ct);

            if (page.Count == 0)
                break;

            foreach (var r in page)
            {
                // Only tombstone currently active keys.
                if (r.IsDeleted)
                    continue;

                if (keep is not null && keep.Contains(r.DimensionSetId))
                    continue;

                list.Add(new ReferenceRegisterRecordWrite(
                    r.DimensionSetId,
                    r.PeriodUtc,
                    recorderDocumentId,
                    new Dictionary<string, object?>(r.Values, StringComparer.Ordinal),
                    IsDeleted: true));
            }

            after = page[^1].DimensionSetId;
        }

        return list;
    }

    private async Task<IReadOnlyList<AccountingEntry>> GetAccountingEntriesToReverseAsync(
        DocumentRecord doc,
        IReadOnlyList<AccountingEntry> historicalEntries,
        string operation,
        CancellationToken ct)
    {
        if (historicalEntries.Count == 0)
            return [];

        var accountingPostingAction = postingActionResolver.TryResolve(doc);
        if (accountingPostingAction is null)
            return historicalEntries;

        var context = await accountingContextFactory.CreateAsync(ct);
        await accountingPostingAction(context, ct);

        if (context.Entries.Count == 0)
        {
            throw new NgbInvariantViolationException(
                "Posted document has persisted accounting history, but its current accounting snapshot is empty.",
                new Dictionary<string, object?>
                {
                    ["documentId"] = doc.Id,
                    ["typeCode"] = doc.TypeCode,
                    ["operation"] = operation
                });
        }

        return context.Entries.ToList();
    }
    
    private static void EnsureOperationalRegisterExecuted(
        OperationalRegisterWriteResult result,
        Guid registerId,
        Guid documentId,
        OperationalRegisterWriteOperation operation)
    {
        if (result == OperationalRegisterWriteResult.AlreadyCompleted)
            throw BuildSubsystemStateConflict(documentId, "operational-register", registerId, operation.ToString());
    }

    private static NgbInvariantViolationException BuildSubsystemStateConflict(
        Guid documentId,
        string layer,
        Guid registerId,
        string operation)
        => new(
            "Document lifecycle state is inconsistent with subsystem technical state.",
            new Dictionary<string, object?>
            {
                ["documentId"] = documentId,
                ["layer"] = layer,
                ["registerId"] = registerId,
                ["operation"] = operation
            });

}
