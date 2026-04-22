using System.Globalization;
using Microsoft.Extensions.Logging;
using NGB.Accounting.Accounts;
using NGB.Accounting.Documents;
using NGB.Accounting.PostingState;
using NGB.Core.AuditLog;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents.Numbering;
using NGB.Runtime.Documents.Policies;
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;
using NGB.Runtime.UnitOfWork;
using NGB.Runtime.Posting;
using NGB.Runtime.Dimensions;
using NGB.Tools.Extensions;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.GeneralJournalEntry;

public sealed class GeneralJournalEntryDocumentService(
    IUnitOfWork uow,
    IAdvisoryLockManager locks,
    IDocumentRepository documents,
    IDocumentDraftService draftService,
    IDocumentWorkflowExecutor workflow,
    IGeneralJournalEntryRepository gje,
    IDocumentRelationshipService relationships,
    IDocumentNumberingAndTypedSyncService numberingSync,
    IDocumentApprovalPolicyResolver approvalPolicies,
    IChartOfAccountsProvider coaProvider,
    IDimensionSetService dimensionSets,
    IDimensionSetReader dimensionSetReader,
    PostingEngine postingEngine,
    ILogger<GeneralJournalEntryDocumentService> logger,
    TimeProvider timeProvider,
    IAuditLogService audit)
    : IGeneralJournalEntryDocumentService
{
    public async Task<GeneralJournalEntryDraftSnapshot> GetDraftAsync(Guid documentId, CancellationToken ct = default)
    {
        const string op = "GeneralJournalEntry.GetDraft";

        var doc = await documents.GetAsync(documentId, ct)
                  ?? throw new DocumentNotFoundException(documentId);

        if (!string.Equals(doc.TypeCode, AccountingDocumentTypeCodes.GeneralJournalEntry, StringComparison.Ordinal))
            throw new DocumentTypeMismatchException(documentId, expectedTypeCode: AccountingDocumentTypeCodes.GeneralJournalEntry, actualTypeCode: doc.TypeCode);

        if (doc.Status != DocumentStatus.Draft)
            throw new DocumentWorkflowStateMismatchException(
                operation: op,
                documentId: documentId,
                expectedState: nameof(DocumentStatus.Draft),
                actualState: doc.Status.ToString());

        var header = await gje.GetHeaderAsync(documentId, ct)
                     ?? throw new GeneralJournalEntryTypedHeaderNotFoundException(op, documentId);

        var lines = await gje.GetLinesAsync(documentId, ct);

        var setIds = lines
            .Select(x => x.DimensionSetId)
            .Distinct()
            .ToArray();

        var bagsById = await dimensionSetReader.GetBagsByIdsAsync(setIds, ct);

        var projected = lines
            .Select(l => new GeneralJournalEntryDraftLineSnapshot(
                LineNo: l.LineNo,
                Side: l.Side,
                AccountId: l.AccountId,
                Amount: l.Amount,
                Memo: l.Memo,
                DimensionSetId: l.DimensionSetId,
                Dimensions: bagsById.TryGetValue(l.DimensionSetId, out var bag) ? bag : DimensionBag.Empty))
            .ToList();

        return new GeneralJournalEntryDraftSnapshot(doc, header, projected);
    }

    public async Task<Guid> CreateDraftAsync(
        DateTime dateUtc,
        string initiatedBy,
        CancellationToken ct = default,
        Guid? createdFromDocumentId = null,
        IReadOnlyList<Guid>? basedOnDocumentIds = null)
    {
        dateUtc.EnsureUtc(nameof(dateUtc));
        if (string.IsNullOrWhiteSpace(initiatedBy))
            throw new NgbArgumentRequiredException(nameof(initiatedBy));

        const string op = "GeneralJournalEntry.CreateDraft";
        
        return await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            // Create the common document row and typed draft storage in a single atomic transaction.
            var documentId = await draftService.CreateDraftAsync(
                AccountingDocumentTypeCodes.GeneralJournalEntry,
                number: null,
                dateUtc: dateUtc,
                manageTransaction: false,
                ct: innerCt);

            // Persist initiator on the typed header.
            await locks.LockDocumentAsync(documentId, innerCt);
            var header = await EnsureHeaderForUpdateAsync(op, documentId, innerCt);

            var now = timeProvider.GetUtcNowDateTime();
            var updated = header with
            {
                InitiatedBy = initiatedBy,
                InitiatedAtUtc = now,
                UpdatedAtUtc = now
            };

            await gje.UpsertHeaderAsync(updated, innerCt);
            await CreateRelationshipsAsync(documentId, createdFromDocumentId, basedOnDocumentIds, innerCt);

            return documentId;
        }, ct);
    }

    public async Task<Guid> CreateAndPostApprovedAsync(
        DateTime dateUtc,
        GeneralJournalEntryDraftHeaderUpdate? header,
        IReadOnlyList<GeneralJournalEntryDraftLineInput>? lines,
        string initiatedBy,
        string submittedBy,
        string approvedBy,
        string postedBy,
        CancellationToken ct = default,
        Guid? createdFromDocumentId = null,
        IReadOnlyList<Guid>? basedOnDocumentIds = null)
    {
        dateUtc.EnsureUtc(nameof(dateUtc));
        
        if (string.IsNullOrWhiteSpace(initiatedBy))
            throw new NgbArgumentRequiredException(nameof(initiatedBy));
        
        if (string.IsNullOrWhiteSpace(submittedBy))
            throw new NgbArgumentRequiredException(nameof(submittedBy));
        
        if (string.IsNullOrWhiteSpace(approvedBy))
            throw new NgbArgumentRequiredException(nameof(approvedBy));
        
        if (string.IsNullOrWhiteSpace(postedBy))
            throw new NgbArgumentRequiredException(nameof(postedBy));

        var createdId = Guid.Empty;
        var op = GeneralJournalEntryWorkflowOperationNames.CreateAndPostApproved;

        await workflow.ExecuteAsync(
            operationName: op,
            documentId: null,
            action: async innerCt =>
            {
                // 0) Create draft (common row + typed storage) inside the current transaction.
                var documentId = await draftService.CreateDraftAsync(
                    AccountingDocumentTypeCodes.GeneralJournalEntry,
                    number: null,
                    dateUtc: dateUtc,
                    manageTransaction: false,
                    ct: innerCt);

                createdId = documentId;

                // Lock + load draft and typed header.
                await locks.LockDocumentAsync(documentId, innerCt);
                var (doc, currentHeader) = await LoadDraftForUpdateAsync(op, documentId, innerCt);

                var now = timeProvider.GetUtcNowDateTime();

                // 1) Apply initiator + optional header patch.
                var patchedHeader = currentHeader with
                {
                    InitiatedBy = initiatedBy,
                    InitiatedAtUtc = now,
                    JournalType = header?.JournalType ?? currentHeader.JournalType,
                    ReasonCode = header?.ReasonCode ?? currentHeader.ReasonCode,
                    Memo = header?.Memo ?? currentHeader.Memo,
                    ExternalReference = header?.ExternalReference ?? currentHeader.ExternalReference,
                    AutoReverse = header?.AutoReverse ?? currentHeader.AutoReverse,
                    AutoReverseOnUtc = header?.AutoReverseOnUtc ?? currentHeader.AutoReverseOnUtc,
                    UpdatedAtUtc = now,
                };

                ValidateDraftHeader(op, documentId, doc, patchedHeader);
                await gje.UpsertHeaderAsync(patchedHeader, innerCt);
                await CreateRelationshipsAsync(documentId, createdFromDocumentId, basedOnDocumentIds, innerCt);

                // 2) Optional lines replacement.
                IReadOnlyList<GeneralJournalEntryLineRecord> effectiveLines;

                if (lines is not null && lines.Count > 0)
                {
                    var normalized = await NormalizeAndResolveLinesAsync(op, documentId, lines, innerCt);
                    await ValidateLinesAgainstChartOfAccountsAsync(op, documentId, normalized, innerCt);

                    await gje.ReplaceLinesAsync(documentId, normalized, innerCt);
                    await gje.ReplaceAllocationsAsync(documentId, Array.Empty<GeneralJournalEntryAllocationRecord>(), innerCt);

                    await gje.TouchUpdatedAtAsync(documentId, now, innerCt);
                    await TouchDocumentUpdatedAtAsync(doc, now, innerCt);

                    effectiveLines = normalized;
                }
                else
                {
                    effectiveLines = await gje.GetLinesAsync(documentId, innerCt);
                }

                // 3) Submit
                EnsureBusinessFieldsArePresent(op, documentId, patchedHeader);
                ValidateBalancedLines(op, documentId, effectiveLines);

                var submittedHeader = patchedHeader with
                {
                    ApprovalState = GeneralJournalEntryModels.ApprovalState.Submitted,
                    SubmittedBy = submittedBy,
                    SubmittedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                ValidateDraftHeader(op, documentId, doc, submittedHeader);
                await numberingSync.EnsureNumberAndSyncTypedAsync(doc, now, innerCt);
                await gje.UpsertHeaderAsync(submittedHeader, innerCt);
                await TouchDocumentUpdatedAtAsync(doc, now, innerCt);

                // 4) Approve
                var approvedHeader = submittedHeader with
                {
                    ApprovalState = GeneralJournalEntryModels.ApprovalState.Approved,
                    ApprovedBy = approvedBy,
                    ApprovedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                ValidateDraftHeader(op, documentId, doc, approvedHeader);
                await numberingSync.EnsureNumberAndSyncTypedAsync(doc, now, innerCt);
                await gje.UpsertHeaderAsync(approvedHeader, innerCt);
                await TouchDocumentUpdatedAtAsync(doc, now, innerCt);

                // 5) Post (document-level approval enforcement; engine stays neutral)
                var approvalPolicy = approvalPolicies.Resolve(doc.TypeCode);
                if (approvalPolicy is not null)
                    await approvalPolicy.EnsureCanPostAsync(doc, innerCt);

                await ValidateLinesAgainstChartOfAccountsAsync(op, documentId, effectiveLines, innerCt);

                var allocations = BuildAllocations(op, documentId, effectiveLines);
                var linesByNo = effectiveLines.ToDictionary(l => l.LineNo);

                var lineDimensionSetIds = effectiveLines
                    .Select(l => l.DimensionSetId)
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToArray();

                var bagsById = lineDimensionSetIds.Length == 0
                    ? new Dictionary<Guid, DimensionBag>()
                    : await dimensionSetReader.GetBagsByIdsAsync(lineDimensionSetIds, innerCt);

                await postingEngine.PostAsync(
                    PostingOperation.Post,
                    async (ctx, postingCt) =>
                    {
                        var chart = await ctx.GetChartOfAccountsAsync(postingCt);

                        foreach (var a in allocations)
                        {
                            var debitLine = linesByNo[a.DebitLineNo];
                            var creditLine = linesByNo[a.CreditLineNo];

                            var debitAcc = chart.Get(debitLine.AccountId);
                            var creditAcc = chart.Get(creditLine.AccountId);

                            var debitBag = bagsById.TryGetValue(debitLine.DimensionSetId, out var db)
                                ? db
                                : DimensionBag.Empty;
                            
                            var creditBag = bagsById.TryGetValue(creditLine.DimensionSetId, out var cb)
                                ? cb
                                : DimensionBag.Empty;

                            ctx.Post(
                                documentId: documentId,
                                period: doc.DateUtc,
                                debit: debitAcc,
                                credit: creditAcc,
                                amount: a.Amount,
                                debitDimensions: debitBag,
                                creditDimensions: creditBag,
                                isStorno: false);

                            // Preserve per-line resolved DimensionSetId (PostingEngine does not overwrite non-empty IDs).
                            var entry = ctx.Entries[^1];
                            entry.DebitDimensionSetId = debitLine.DimensionSetId;
                            entry.CreditDimensionSetId = creditLine.DimensionSetId;
                        }
                    },
                    manageTransaction: false,
                    innerCt);

                await gje.ReplaceAllocationsAsync(documentId, allocations, innerCt);

                // Persist posting audit on typed header while the document is still Draft.
                await gje.UpsertHeaderAsync(approvedHeader with
                {
                    PostedBy = postedBy,
                    PostedAtUtc = now,
                    UpdatedAtUtc = now,
                }, innerCt);

                // Auto reversal scheduling
                if (approvedHeader is { Source: GeneralJournalEntryModels.Source.Manual, AutoReverse: true })
                {
                    if (approvedHeader.AutoReverseOnUtc is null)
                        throw new GeneralJournalEntryAutoReverseOnUtcRequiredException(op, documentId);

                    await EnsureSystemReversalCreatedAsync(op, documentId, approvedHeader.AutoReverseOnUtc.Value, initiatedBy: "SYSTEM", innerCt);
                }

                await documents.UpdateStatusAsync(documentId, DocumentStatus.Posted, now, now, doc.MarkedForDeletionAtUtc, innerCt);

                return true;
            },
            manageTransaction: true,
            ct: ct);

        return createdId;
    }

    public async Task UpdateDraftHeaderAsync(
        Guid documentId,
        GeneralJournalEntryDraftHeaderUpdate update,
        string updatedBy,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(updatedBy))
            throw new NgbArgumentRequiredException(nameof(updatedBy));

        if (update is null)
            throw new NgbArgumentRequiredException(nameof(update));

        await workflow.ExecuteAsync(
            operationName: GeneralJournalEntryWorkflowOperationNames.UpdateDraftHeader,
            documentId: documentId,
            action: async innerCt =>
            {
                var op = GeneralJournalEntryWorkflowOperationNames.UpdateDraftHeader;

                var (doc, header) = await LoadDraftForUpdateAsync(op, documentId, innerCt);

                if (header.Source == GeneralJournalEntryModels.Source.System)
                    throw new GeneralJournalEntrySystemDocumentOperationForbiddenException(op, documentId);

                if (header.ApprovalState != GeneralJournalEntryModels.ApprovalState.Draft)
                    throw new DocumentWorkflowStateMismatchException(
                        operation: op,
                        documentId: documentId,
                        expectedState: "Draft",
                        actualState: header.ApprovalState.ToString());

                var now = timeProvider.GetUtcNowDateTime();
                var newHeader = header with
                {
                    JournalType = update.JournalType ?? header.JournalType,
                    ReasonCode = update.ReasonCode ?? header.ReasonCode,
                    Memo = update.Memo ?? header.Memo,
                    ExternalReference = update.ExternalReference ?? header.ExternalReference,
                    AutoReverse = update.AutoReverse ?? header.AutoReverse,
                    AutoReverseOnUtc = update.AutoReverseOnUtc ?? header.AutoReverseOnUtc,
                    UpdatedAtUtc = now,
                };

                ValidateDraftHeader(op, documentId, doc, newHeader);

                await gje.UpsertHeaderAsync(newHeader, innerCt);
                await TouchDocumentUpdatedAtAsync(doc, now, innerCt);

                var changes = BuildHeaderAuditChanges(header, newHeader);
                await WriteDocumentAuditAsync(
                    doc,
                    AuditActionCodes.DocumentUpdateDraft,
                    changes,
                    innerCt);

                return true;
            },
            manageTransaction: true,
            ct: ct);
    }

    public async Task ReplaceDraftLinesAsync(
        Guid documentId,
        IReadOnlyList<GeneralJournalEntryDraftLineInput> lines,
        string updatedBy,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(updatedBy))
            throw new NgbArgumentRequiredException(nameof(updatedBy));

        if (lines is null)
            throw new NgbArgumentRequiredException(nameof(lines));

        await workflow.ExecuteAsync(
            operationName: GeneralJournalEntryWorkflowOperationNames.ReplaceDraftLines,
            documentId: documentId,
            action: async innerCt =>
            {
                var op = GeneralJournalEntryWorkflowOperationNames.ReplaceDraftLines;

                var (doc, header) = await LoadDraftForUpdateAsync(op, documentId, innerCt);

                if (header.Source == GeneralJournalEntryModels.Source.System)
                    throw new GeneralJournalEntrySystemDocumentOperationForbiddenException(op, documentId);

                if (header.ApprovalState != GeneralJournalEntryModels.ApprovalState.Draft)
                    throw new DocumentWorkflowStateMismatchException(
                        operation: op,
                        documentId: documentId,
                        expectedState: "Draft",
                        actualState: header.ApprovalState.ToString());

                var existingLines = await gje.GetLinesAsync(documentId, innerCt);
                var normalized = await NormalizeAndResolveLinesAsync(op, documentId, lines, innerCt);

                // Validate account/dimension constraints early (posting validator also enforces, but we want nice errors).
                await ValidateLinesAgainstChartOfAccountsAsync(op, documentId, normalized, innerCt);

                await gje.ReplaceLinesAsync(documentId, normalized, innerCt);
                await gje.ReplaceAllocationsAsync(documentId, [], innerCt);

                var now = timeProvider.GetUtcNowDateTime();

                await gje.TouchUpdatedAtAsync(documentId, now, innerCt);
                await TouchDocumentUpdatedAtAsync(doc, now, innerCt);

                var changes = BuildLineAuditChanges(existingLines, normalized);
                await WriteDocumentAuditAsync(
                    doc,
                    AuditActionCodes.DocumentUpdateDraft,
                    changes,
                    innerCt);

                return true;
            },
            manageTransaction: true,
            ct: ct);
    }

    public async Task SubmitAsync(Guid documentId, string submittedBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(submittedBy))
            throw new NgbArgumentRequiredException(nameof(submittedBy));

        await workflow.ExecuteAsync(
            operationName: GeneralJournalEntryWorkflowOperationNames.Submit,
            documentId: documentId,
            action: async innerCt =>
            {
                var op = GeneralJournalEntryWorkflowOperationNames.Submit;

                var (doc, header) = await LoadDraftForUpdateAsync(op, documentId, innerCt);

                if (header.Source == GeneralJournalEntryModels.Source.System)
                    throw new GeneralJournalEntrySystemDocumentOperationForbiddenException(op, documentId);

                if (header.ApprovalState != GeneralJournalEntryModels.ApprovalState.Draft)
                    throw new DocumentWorkflowStateMismatchException(
                        operation: op,
                        documentId: documentId,
                        expectedState: "Draft",
                        actualState: header.ApprovalState.ToString());

                EnsureBusinessFieldsArePresent(op, documentId, header);

                var lines = await gje.GetLinesAsync(documentId, innerCt);
                ValidateBalancedLines(op, documentId, lines);

                var now = timeProvider.GetUtcNowDateTime();
                var updated = header with
                {
                    ApprovalState = GeneralJournalEntryModels.ApprovalState.Submitted,
                    SubmittedBy = submittedBy,
                    SubmittedAtUtc = now,
                    UpdatedAtUtc = now,
                };

                ValidateDraftHeader(op, documentId, doc, updated);

                // Assign document number on Submit (per type + fiscal year), inside the same transaction.
                var assignedNumber = await numberingSync.EnsureNumberAndSyncTypedAsync(doc, now, innerCt);
                var effectiveNumber = string.IsNullOrWhiteSpace(doc.Number) ? assignedNumber : doc.Number;

                await gje.UpsertHeaderAsync(updated, innerCt);
                await TouchDocumentUpdatedAtAsync(doc, now, innerCt);

                var changes = BuildHeaderAuditChanges(header, updated);
                AppendChangeIfDifferent(changes, "number", doc.Number, effectiveNumber);

                await WriteDocumentAuditAsync(
                    doc,
                    AuditActionCodes.DocumentSubmit,
                    changes,
                    innerCt,
                    effectiveNumber);

                return true;
            },
            manageTransaction: true,
            ct: ct);
    }

    public async Task ApproveAsync(Guid documentId, string approvedBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(approvedBy))
            throw new NgbArgumentRequiredException(nameof(approvedBy));

        await workflow.ExecuteAsync(
            operationName: GeneralJournalEntryWorkflowOperationNames.Approve,
            documentId: documentId,
            action: async innerCt =>
            {
                var op = GeneralJournalEntryWorkflowOperationNames.Approve;

                var (doc, header) = await LoadDraftForUpdateAsync(op, documentId, innerCt);

                if (header.Source == GeneralJournalEntryModels.Source.System)
                    throw new GeneralJournalEntrySystemDocumentOperationForbiddenException(op, documentId);

                if (header.ApprovalState != GeneralJournalEntryModels.ApprovalState.Submitted)
                    throw new DocumentWorkflowStateMismatchException(
                        operation: op,
                        documentId: documentId,
                        expectedState: "Submitted",
                        actualState: header.ApprovalState.ToString());

                var lines = await gje.GetLinesAsync(documentId, innerCt);
                ValidateBalancedLines(op, documentId, lines);

                var now = timeProvider.GetUtcNowDateTime();
                var updated = header with
                {
                    ApprovalState = GeneralJournalEntryModels.ApprovalState.Approved,
                    ApprovedBy = approvedBy,
                    ApprovedAtUtc = now,
                    UpdatedAtUtc = now,
                };

                ValidateDraftHeader(op, documentId, doc, updated);
                var assignedNumber = await numberingSync.EnsureNumberAndSyncTypedAsync(doc, now, innerCt);
                var effectiveNumber = string.IsNullOrWhiteSpace(doc.Number) ? assignedNumber : doc.Number;

                await gje.UpsertHeaderAsync(updated, innerCt);
                await TouchDocumentUpdatedAtAsync(doc, now, innerCt);

                var changes = BuildHeaderAuditChanges(header, updated);
                AppendChangeIfDifferent(changes, "number", doc.Number, effectiveNumber);

                await WriteDocumentAuditAsync(
                    doc,
                    AuditActionCodes.DocumentApprove,
                    changes,
                    innerCt,
                    effectiveNumber);

                return true;
            },
            manageTransaction: true,
            ct: ct);
    }

    public async Task RejectAsync(
        Guid documentId,
        string rejectedBy,
        string rejectReason,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rejectedBy))
            throw new NgbArgumentRequiredException(nameof(rejectedBy));

        if (string.IsNullOrWhiteSpace(rejectReason))
            throw new NgbArgumentRequiredException(nameof(rejectReason));

        await workflow.ExecuteAsync(
            operationName: GeneralJournalEntryWorkflowOperationNames.Reject,
            documentId: documentId,
            action: async innerCt =>
            {
                var op = GeneralJournalEntryWorkflowOperationNames.Reject;

                var (doc, header) = await LoadDraftForUpdateAsync(op, documentId, innerCt);

                if (header.Source == GeneralJournalEntryModels.Source.System)
                    throw new GeneralJournalEntrySystemDocumentOperationForbiddenException(op, documentId);

                if (header.ApprovalState != GeneralJournalEntryModels.ApprovalState.Submitted)
                    throw new DocumentWorkflowStateMismatchException(
                        operation: op,
                        documentId: documentId,
                        expectedState: "Submitted",
                        actualState: header.ApprovalState.ToString());

                var now = timeProvider.GetUtcNowDateTime();
                var updated = header with
                {
                    ApprovalState = GeneralJournalEntryModels.ApprovalState.Rejected,
                    RejectedBy = rejectedBy,
                    RejectedAtUtc = now,
                    RejectReason = rejectReason,
                    UpdatedAtUtc = now,
                };

                await gje.UpsertHeaderAsync(updated, innerCt);
                await TouchDocumentUpdatedAtAsync(doc, now, innerCt);

                var changes = BuildHeaderAuditChanges(header, updated);
                await WriteDocumentAuditAsync(
                    doc,
                    AuditActionCodes.DocumentReject,
                    changes,
                    innerCt);

                return true;
            },
            manageTransaction: true,
            ct: ct);
    }

    public async Task PostApprovedAsync(Guid documentId, string postedBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(postedBy))
            throw new NgbArgumentRequiredException(nameof(postedBy));

        await workflow.ExecuteAsync(
            operationName: GeneralJournalEntryWorkflowOperationNames.PostApproved,
            documentId: documentId,
            action: async innerCt =>
            {
                var op = GeneralJournalEntryWorkflowOperationNames.PostApproved;

                var doc = await documents.GetForUpdateAsync(documentId, innerCt)
                          ?? throw new DocumentNotFoundException(documentId);

                if (!string.Equals(doc.TypeCode, AccountingDocumentTypeCodes.GeneralJournalEntry, StringComparison.Ordinal))
                    throw new DocumentTypeMismatchException(documentId, expectedTypeCode: AccountingDocumentTypeCodes.GeneralJournalEntry, actualTypeCode: doc.TypeCode);

                if (doc.Status == DocumentStatus.Posted)
                {
                    logger.LogInformation("GJE {DocumentId} already posted.", documentId);
                    return false;
                }

                if (doc.MarkedForDeletionAtUtc is not null || doc.Status == DocumentStatus.MarkedForDeletion)
                {
                    var markedAt = doc.MarkedForDeletionAtUtc ?? doc.UpdatedAtUtc;
                    throw new DocumentMarkedForDeletionException(op, documentId, markedAt);
                }

                if (doc.Status != DocumentStatus.Draft)
                    throw new DocumentWorkflowStateMismatchException(
                        operation: op,
                        documentId: documentId,
                        expectedState: nameof(DocumentStatus.Draft),
                        actualState: doc.Status.ToString());

                var header = await EnsureHeaderForUpdateAsync(op, documentId, innerCt);

                // Approval is a document-level responsibility (engine stays neutral).
                // For GJE, this rule is expressed via Definitions-backed approval policy.
                var approvalPolicy = approvalPolicies.Resolve(doc.TypeCode);
                if (approvalPolicy is not null)
                    await approvalPolicy.EnsureCanPostAsync(doc, innerCt);

                EnsureBusinessFieldsArePresent(op, documentId, header);

                var lines = await gje.GetLinesAsync(documentId, innerCt);
                ValidateBalancedLines(op, documentId, lines);
                await ValidateLinesAgainstChartOfAccountsAsync(op, documentId, lines, innerCt);

                var now = timeProvider.GetUtcNowDateTime();
                var assignedNumber = await numberingSync.EnsureNumberAndSyncTypedAsync(doc, now, innerCt);
                var effectiveNumber = string.IsNullOrWhiteSpace(doc.Number) ? assignedNumber : doc.Number;

                var allocations = BuildAllocations(op, documentId, lines);
                var linesByNo = lines.ToDictionary(l => l.LineNo);

                var lineDimensionSetIds = lines
                    .Select(l => l.DimensionSetId)
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToArray();

                var bagsById = lineDimensionSetIds.Length == 0
                    ? new Dictionary<Guid, DimensionBag>()
                    : await dimensionSetReader.GetBagsByIdsAsync(lineDimensionSetIds, innerCt);

                // 1) Write accounting movements (idempotent per posting_log)
                await postingEngine.PostAsync(
                    PostingOperation.Post,
                    async (ctx, postingCt) =>
                    {
                        var chart = await ctx.GetChartOfAccountsAsync(postingCt);

                        foreach (var a in allocations)
                        {
                            var debitLine = linesByNo[a.DebitLineNo];
                            var creditLine = linesByNo[a.CreditLineNo];

                            var debitAcc = chart.Get(debitLine.AccountId);
                            var creditAcc = chart.Get(creditLine.AccountId);

                            var debitBag = bagsById.TryGetValue(debitLine.DimensionSetId, out var db)
                                ? db
                                : DimensionBag.Empty;
                            
                            var creditBag = bagsById.TryGetValue(creditLine.DimensionSetId, out var cb)
                                ? cb
                                : DimensionBag.Empty;

                            ctx.Post(
                                documentId: documentId,
                                period: doc.DateUtc,
                                debit: debitAcc,
                                credit: creditAcc,
                                amount: a.Amount,
                                debitDimensions: debitBag,
                                creditDimensions: creditBag,
                                isStorno: false);

                            // Preserve per-line resolved DimensionSetId (PostingEngine does not overwrite non-empty IDs).
                            var entry = ctx.Entries[^1];
                            entry.DebitDimensionSetId = debitLine.DimensionSetId;
                            entry.CreditDimensionSetId = creditLine.DimensionSetId;
                        }
                    },
                    manageTransaction: false,
                    innerCt);

                // 2) Persist allocation map (audit + explainability)
                await gje.ReplaceAllocationsAsync(documentId, allocations, innerCt);

                // 3) Persist posting audit on typed header while the document is still Draft.
                // IMPORTANT: DB immutability guards forbid mutating typed storages of already posted documents.
                var postedHeader = header with
                {
                    PostedBy = postedBy,
                    PostedAtUtc = now,
                    UpdatedAtUtc = now
                };

                await gje.UpsertHeaderAsync(postedHeader, innerCt);

                // 4) Auto reversal scheduling (creates a system reversal doc as Draft+Approved)
                if (header is { Source: GeneralJournalEntryModels.Source.Manual, AutoReverse: true })
                {
                    if (header.AutoReverseOnUtc is null)
                        throw new GeneralJournalEntryAutoReverseOnUtcRequiredException(op, documentId);

                    await EnsureSystemReversalCreatedAsync(op, documentId, header.AutoReverseOnUtc.Value, initiatedBy: "SYSTEM", innerCt);
                }

                // 5) Mark document posted (must be last, after typed storages were fully persisted)
                await documents.UpdateStatusAsync(documentId, DocumentStatus.Posted, now, now, doc.MarkedForDeletionAtUtc, innerCt);

                var changes = BuildHeaderAuditChanges(header, postedHeader);
                AppendChangeIfDifferent(changes, "status", doc.Status, DocumentStatus.Posted);
                AppendChangeIfDifferent(changes, "posted_at_utc", doc.PostedAtUtc, now);
                AppendChangeIfDifferent(changes, "number", doc.Number, effectiveNumber);
                if (doc.MarkedForDeletionAtUtc is not null)
                    AppendChangeIfDifferent(changes, "marked_for_deletion_at_utc", doc.MarkedForDeletionAtUtc, null);

                await WriteDocumentAuditAsync(
                    doc,
                    AuditActionCodes.DocumentPost,
                    changes,
                    innerCt,
                    effectiveNumber);

                return true;
            },
            manageTransaction: true,
            ct: ct);
    }

    public async Task<Guid> ReversePostedAsync(
        Guid originalDocumentId,
        DateTime reversalDateUtc,
        string initiatedBy,
        bool postImmediately = true,
        CancellationToken ct = default)
    {
        reversalDateUtc.EnsureUtc(nameof(reversalDateUtc));
        if (string.IsNullOrWhiteSpace(initiatedBy))
            throw new NgbArgumentRequiredException(nameof(initiatedBy));

        var reversalId = Guid.Empty;
        var created = false;

        await workflow.ExecuteAsync(
            operationName: GeneralJournalEntryWorkflowOperationNames.ReversePosted,
            documentId: originalDocumentId,
            action: async innerCt =>
            {
                var op = GeneralJournalEntryWorkflowOperationNames.ReversePosted;

                var originalDoc = await documents.GetForUpdateAsync(originalDocumentId, innerCt)
                                 ?? throw new DocumentNotFoundException(originalDocumentId);

                if (!string.Equals(originalDoc.TypeCode, AccountingDocumentTypeCodes.GeneralJournalEntry, StringComparison.Ordinal))
                    throw new DocumentTypeMismatchException(originalDocumentId, expectedTypeCode: AccountingDocumentTypeCodes.GeneralJournalEntry, actualTypeCode: originalDoc.TypeCode);

                if (originalDoc.Status != DocumentStatus.Posted)
                    throw new DocumentWorkflowStateMismatchException(
                        operation: op,
                        documentId: originalDocumentId,
                        expectedState: nameof(DocumentStatus.Posted),
                        actualState: originalDoc.Status.ToString());

                var existing = await gje.TryGetSystemReversalByOriginalAsync(originalDocumentId, innerCt);
                if (existing is not null)
                {
                    logger.LogInformation(
                        "Reversal already exists for {OriginalDocumentId}: {ReversalDocumentId}",
                        originalDocumentId,
                        existing);

                    reversalId = existing.Value;
                    created = false;
                    return false;
                }

                await EnsureHeaderForUpdateAsync(op, originalDocumentId, innerCt);
                var originalLines = await gje.GetLinesAsync(originalDocumentId, innerCt);
                ValidateBalancedLines(op, originalDocumentId, originalLines);

                var reversalDateOnly = DateOnly.FromDateTime(reversalDateUtc);
                reversalId = DeterministicGuid.Create($"gje:reversal:{originalDocumentId:N}:{reversalDateOnly:yyyy-MM-dd}");

                await locks.LockDocumentAsync(reversalId, innerCt);

                // Create reversal draft header
                var now = timeProvider.GetUtcNowDateTime();
                var reversalDoc = new DocumentRecord
                {
                    Id = reversalId,
                    TypeCode = AccountingDocumentTypeCodes.GeneralJournalEntry,
                    DateUtc = new DateTime(reversalDateOnly.Year, reversalDateOnly.Month, reversalDateOnly.Day, 0, 0, 0, DateTimeKind.Utc),
                    Status = DocumentStatus.Draft,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    Number = null,
                    PostedAtUtc = null,
                    MarkedForDeletionAtUtc = null,
                };

                await documents.CreateAsync(reversalDoc, innerCt);

                var originalDisplay = BuildDisplay(originalDoc);

                var reversalHeader = new GeneralJournalEntryHeaderRecord(
                    reversalId,
                    GeneralJournalEntryModels.JournalType.Reversing,
                    GeneralJournalEntryModels.Source.System,
                    GeneralJournalEntryModels.ApprovalState.Approved,
                    ReasonCode: "REVERSAL",
                    Memo: $"Reversal of {originalDisplay}",
                    ExternalReference: null,
                    AutoReverse: false,
                    AutoReverseOnUtc: null,
                    ReversalOfDocumentId: originalDocumentId,
                    InitiatedBy: initiatedBy,
                    InitiatedAtUtc: now,
                    SubmittedBy: initiatedBy,
                    SubmittedAtUtc: now,
                    ApprovedBy: initiatedBy,
                    ApprovedAtUtc: now,
                    RejectedBy: null,
                    RejectedAtUtc: null,
                    RejectReason: null,
                    PostedBy: null,
                    PostedAtUtc: null,
                    CreatedAtUtc: now,
                    UpdatedAtUtc: now);

                await gje.UpsertHeaderAsync(reversalHeader, innerCt);
                await CreateSystemReversalRelationshipsAsync(reversalId, originalDocumentId, innerCt);

                var reversalLines = originalLines
                    .Select(l => l with
                    {
                        DocumentId = reversalId,
                        Side = l.Side == GeneralJournalEntryModels.LineSide.Debit
                            ? GeneralJournalEntryModels.LineSide.Credit
                            : GeneralJournalEntryModels.LineSide.Debit
                    })
                    .ToList();

                await gje.ReplaceLinesAsync(reversalId, reversalLines, innerCt);

                // allocations empty until posted
                await gje.ReplaceAllocationsAsync(reversalId, [], innerCt);

                await WriteDocumentAuditAsync(
                    reversalDoc,
                    AuditActionCodes.DocumentCreateDraft,
                    BuildDocumentCreateAuditChanges(reversalDoc, reversalHeader),
                    innerCt);

                created = true;
                return true;
            },
            manageTransaction: true,
            ct: ct);

        if (created && postImmediately)
        {
            // Post in a separate transaction (keeps this method simple; posting is idempotent anyway).
            await PostApprovedAsync(reversalId, postedBy: initiatedBy, ct);
        }

        return reversalId;
    }

    private async Task EnsureSystemReversalCreatedAsync(
        string operation,
        Guid originalDocumentId,
        DateOnly autoReverseOnUtc,
        string initiatedBy,
        CancellationToken ct)
    {
        var existing = await gje.TryGetSystemReversalByOriginalAsync(originalDocumentId, ct);
        if (existing is not null)
            return;

        var reversalId = DeterministicGuid.Create($"gje:auto-reversal:{originalDocumentId:N}:{autoReverseOnUtc:yyyy-MM-dd}");

        // Double-lock to avoid races if caller ends up being concurrent.
        await locks.LockDocumentAsync(reversalId, ct);

        var now = timeProvider.GetUtcNowDateTime();
        var reversalDoc = new DocumentRecord
        {
            Id = reversalId,
            TypeCode = AccountingDocumentTypeCodes.GeneralJournalEntry,
            DateUtc = new DateTime(autoReverseOnUtc.Year, autoReverseOnUtc.Month, autoReverseOnUtc.Day, 0, 0, 0, DateTimeKind.Utc),
            Status = DocumentStatus.Draft,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Number = null,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null,
        };

        await documents.CreateAsync(reversalDoc, ct);

        var originalDoc = await documents.GetAsync(originalDocumentId, ct)
                         ?? throw new DocumentNotFoundException(originalDocumentId);

        if (!string.Equals(originalDoc.TypeCode, AccountingDocumentTypeCodes.GeneralJournalEntry, StringComparison.Ordinal))
            throw new DocumentTypeMismatchException(originalDocumentId, expectedTypeCode: AccountingDocumentTypeCodes.GeneralJournalEntry, actualTypeCode: originalDoc.TypeCode);

        var originalDisplay = BuildDisplay(originalDoc);

        var reversalHeader = new GeneralJournalEntryHeaderRecord(
            reversalId,
            GeneralJournalEntryModels.JournalType.Reversing,
            GeneralJournalEntryModels.Source.System,
            GeneralJournalEntryModels.ApprovalState.Approved,
            ReasonCode: "AUTO_REVERSAL",
            Memo: $"Auto reversal of {originalDisplay}",
            ExternalReference: null,
            AutoReverse: false,
            AutoReverseOnUtc: null,
            ReversalOfDocumentId: originalDocumentId,
            InitiatedBy: initiatedBy,
            InitiatedAtUtc: now,
            SubmittedBy: "SYSTEM",
            SubmittedAtUtc: now,
            ApprovedBy: "SYSTEM",
            ApprovedAtUtc: now,
            RejectedBy: null,
            RejectedAtUtc: null,
            RejectReason: null,
            PostedBy: null,
            PostedAtUtc: null,
            CreatedAtUtc: now,
            UpdatedAtUtc: now);

        await gje.UpsertHeaderAsync(reversalHeader, ct);
        await CreateSystemReversalRelationshipsAsync(reversalId, originalDocumentId, ct);

        var originalLines = await gje.GetLinesAsync(originalDocumentId, ct);
        var reversalLines = originalLines
            .Select(l => l with
            {
                DocumentId = reversalId,
                Side = l.Side == GeneralJournalEntryModels.LineSide.Debit
                    ? GeneralJournalEntryModels.LineSide.Credit
                    : GeneralJournalEntryModels.LineSide.Debit
            })
            .ToList();

        await gje.ReplaceLinesAsync(reversalId, reversalLines, ct);
        await gje.ReplaceAllocationsAsync(reversalId, [], ct);

        await WriteDocumentAuditAsync(
            reversalDoc,
            AuditActionCodes.DocumentCreateDraft,
            BuildDocumentCreateAuditChanges(reversalDoc, reversalHeader),
            ct);
    }

    private Task WriteDocumentAuditAsync(
        DocumentRecord document,
        string actionCode,
        IReadOnlyList<AuditFieldChange> changes,
        CancellationToken ct,
        string? numberOverride = null)
    {
        if (changes.Count == 0)
            return Task.CompletedTask;

        return audit.WriteAsync(
            entityKind: AuditEntityKind.Document,
            entityId: document.Id,
            actionCode: actionCode,
            changes: changes,
            metadata: new
            {
                typeCode = document.TypeCode,
                number = numberOverride ?? document.Number,
                dateUtc = document.DateUtc
            },
            ct: ct);
    }

    private static List<AuditFieldChange> BuildDocumentCreateAuditChanges(
        DocumentRecord document,
        GeneralJournalEntryHeaderRecord? header = null)
    {
        var changes = new List<AuditFieldChange>
        {
            AuditLogService.Change("type_code", null, document.TypeCode),
            AuditLogService.Change("date_utc", null, document.DateUtc),
            AuditLogService.Change("status", null, document.Status)
        };

        if (!string.IsNullOrWhiteSpace(document.Number))
            changes.Add(AuditLogService.Change("number", null, document.Number));

        if (header is null)
            return changes;

        changes.Add(AuditLogService.Change("journal_type", null, header.JournalType));
        changes.Add(AuditLogService.Change("source", null, header.Source));
        changes.Add(AuditLogService.Change("approval_state", null, header.ApprovalState));
        changes.Add(AuditLogService.Change("auto_reverse", null, header.AutoReverse));

        AppendCreateChangeIfPresent(changes, "reason_code", header.ReasonCode);
        AppendCreateChangeIfPresent(changes, "memo", header.Memo);
        AppendCreateChangeIfPresent(changes, "external_reference", header.ExternalReference);
        AppendCreateChangeIfPresent(changes, "auto_reverse_on_utc", header.AutoReverseOnUtc);
        AppendCreateChangeIfPresent(changes, "reversal_of_document_id", header.ReversalOfDocumentId);
        AppendCreateChangeIfPresent(changes, "initiated_by", header.InitiatedBy);
        AppendCreateChangeIfPresent(changes, "initiated_at_utc", header.InitiatedAtUtc);
        AppendCreateChangeIfPresent(changes, "submitted_by", header.SubmittedBy);
        AppendCreateChangeIfPresent(changes, "submitted_at_utc", header.SubmittedAtUtc);
        AppendCreateChangeIfPresent(changes, "approved_by", header.ApprovedBy);
        AppendCreateChangeIfPresent(changes, "approved_at_utc", header.ApprovedAtUtc);
        AppendCreateChangeIfPresent(changes, "rejected_by", header.RejectedBy);
        AppendCreateChangeIfPresent(changes, "rejected_at_utc", header.RejectedAtUtc);
        AppendCreateChangeIfPresent(changes, "reject_reason", header.RejectReason);
        AppendCreateChangeIfPresent(changes, "posted_by", header.PostedBy);
        AppendCreateChangeIfPresent(changes, "posted_at_utc", header.PostedAtUtc);

        return changes;
    }

    private static List<AuditFieldChange> BuildHeaderAuditChanges(
        GeneralJournalEntryHeaderRecord before,
        GeneralJournalEntryHeaderRecord after)
    {
        var changes = new List<AuditFieldChange>();

        AppendChangeIfDifferent(changes, "journal_type", before.JournalType, after.JournalType);
        AppendChangeIfDifferent(changes, "source", before.Source, after.Source);
        AppendChangeIfDifferent(changes, "approval_state", before.ApprovalState, after.ApprovalState);
        AppendChangeIfDifferent(changes, "reason_code", before.ReasonCode, after.ReasonCode);
        AppendChangeIfDifferent(changes, "memo", before.Memo, after.Memo);
        AppendChangeIfDifferent(changes, "external_reference", before.ExternalReference, after.ExternalReference);
        AppendChangeIfDifferent(changes, "auto_reverse", before.AutoReverse, after.AutoReverse);
        AppendChangeIfDifferent(changes, "auto_reverse_on_utc", before.AutoReverseOnUtc, after.AutoReverseOnUtc);
        AppendChangeIfDifferent(changes, "reversal_of_document_id", before.ReversalOfDocumentId, after.ReversalOfDocumentId);
        AppendChangeIfDifferent(changes, "initiated_by", before.InitiatedBy, after.InitiatedBy);
        AppendChangeIfDifferent(changes, "initiated_at_utc", before.InitiatedAtUtc, after.InitiatedAtUtc);
        AppendChangeIfDifferent(changes, "submitted_by", before.SubmittedBy, after.SubmittedBy);
        AppendChangeIfDifferent(changes, "submitted_at_utc", before.SubmittedAtUtc, after.SubmittedAtUtc);
        AppendChangeIfDifferent(changes, "approved_by", before.ApprovedBy, after.ApprovedBy);
        AppendChangeIfDifferent(changes, "approved_at_utc", before.ApprovedAtUtc, after.ApprovedAtUtc);
        AppendChangeIfDifferent(changes, "rejected_by", before.RejectedBy, after.RejectedBy);
        AppendChangeIfDifferent(changes, "rejected_at_utc", before.RejectedAtUtc, after.RejectedAtUtc);
        AppendChangeIfDifferent(changes, "reject_reason", before.RejectReason, after.RejectReason);
        AppendChangeIfDifferent(changes, "posted_by", before.PostedBy, after.PostedBy);
        AppendChangeIfDifferent(changes, "posted_at_utc", before.PostedAtUtc, after.PostedAtUtc);
        AppendChangeIfDifferent(changes, "updated_at_utc", before.UpdatedAtUtc, after.UpdatedAtUtc);

        return changes;
    }

    private static List<AuditFieldChange> BuildLineAuditChanges(
        IReadOnlyList<GeneralJournalEntryLineRecord> before,
        IReadOnlyList<GeneralJournalEntryLineRecord> after)
    {
        var changes = new List<AuditFieldChange>();
        var beforeByLine = before.ToDictionary(x => x.LineNo);
        var afterByLine = after.ToDictionary(x => x.LineNo);

        foreach (var lineNo in beforeByLine.Keys.Concat(afterByLine.Keys).Distinct().OrderBy(x => x))
        {
            beforeByLine.TryGetValue(lineNo, out var oldLine);
            afterByLine.TryGetValue(lineNo, out var newLine);

            AppendChangeIfDifferent(changes, $"line_{lineNo}_side", oldLine?.Side, newLine?.Side);
            AppendChangeIfDifferent(changes, $"line_{lineNo}_account_id", oldLine?.AccountId, newLine?.AccountId);
            AppendChangeIfDifferent(changes, $"line_{lineNo}_amount", oldLine?.Amount, newLine?.Amount);
            AppendChangeIfDifferent(changes, $"line_{lineNo}_memo", oldLine?.Memo, newLine?.Memo);
            AppendChangeIfDifferent(changes, $"line_{lineNo}_dimension_set_id", oldLine?.DimensionSetId, newLine?.DimensionSetId);
        }

        return changes;
    }

    private static void AppendCreateChangeIfPresent<T>(
        List<AuditFieldChange> changes,
        string fieldPath,
        T? newValue)
    {
        if (newValue is null)
            return;

        changes.Add(AuditLogService.Change(fieldPath, null, newValue));
    }

    private static void AppendChangeIfDifferent<T>(
        List<AuditFieldChange> changes,
        string fieldPath,
        T oldValue,
        T newValue)
    {
        if (EqualityComparer<T>.Default.Equals(oldValue, newValue))
            return;

        changes.Add(AuditLogService.Change(fieldPath, oldValue, newValue));
    }
    
    private static string BuildDisplay(DocumentRecord documentRecord)
    {
        var dateText = documentRecord.DateUtc.ToString("M/d/yyyy", CultureInfo.InvariantCulture);
        var number = documentRecord.Number?.Trim();

        return string.IsNullOrWhiteSpace(number)
            ? $"General Journal Entry {dateText}"
            : $"General Journal Entry {number} {dateText}";
    }

    private async Task CreateRelationshipsAsync(
        Guid fromDocumentId,
        Guid? createdFromDocumentId,
        IReadOnlyList<Guid>? basedOnDocumentIds,
        CancellationToken ct)
    {
        if (createdFromDocumentId is not null)
        {
            await relationships.CreateAsync(
                fromDocumentId,
                createdFromDocumentId.Value,
                relationshipCode: "created_from",
                manageTransaction: false,
                ct: ct);
        }

        if (basedOnDocumentIds is not null && basedOnDocumentIds.Count > 0)
        {
            foreach (var to in basedOnDocumentIds.Where(x => x != Guid.Empty).Distinct())
            {
                await relationships.CreateAsync(
                    fromDocumentId,
                    to,
                    relationshipCode: "based_on",
                    manageTransaction: false,
                    ct: ct);
            }
        }
    }

    private async Task CreateSystemReversalRelationshipsAsync(
        Guid reversalId,
        Guid originalDocumentId,
        CancellationToken ct)
    {
        // reversal_of: semantic link (this document is a reversal of the original)
        await relationships.CreateAsync(
            reversalId,
            originalDocumentId,
            relationshipCode: "reversal_of",
            manageTransaction: false,
            ct: ct);

        // created_from: generic provenance link (this document was created from the original)
        await relationships.CreateAsync(
            reversalId,
            originalDocumentId,
            relationshipCode: "created_from",
            manageTransaction: false,
            ct: ct);
    }

    private static void EnsureBusinessFieldsArePresent(
        string operation,
        Guid documentId,
        GeneralJournalEntryHeaderRecord header)
    {
        if (string.IsNullOrWhiteSpace(header.ReasonCode))
            throw new GeneralJournalEntryBusinessFieldRequiredException(operation, documentId, "ReasonCode");
        
        if (string.IsNullOrWhiteSpace(header.Memo))
            throw new GeneralJournalEntryBusinessFieldRequiredException(operation, documentId, "Memo");
    }
    
    private static void ValidateBalancedLines(
        string operation,
        Guid documentId,
        IReadOnlyList<GeneralJournalEntryLineRecord> lines)
    {
        if (lines.Count == 0)
            throw new GeneralJournalEntryLinesRequiredException(operation, documentId);

        var debit = lines
            .Where(l => l.Side == GeneralJournalEntryModels.LineSide.Debit)
            .Sum(l => l.Amount);

        var credit = lines
            .Where(l => l.Side == GeneralJournalEntryModels.LineSide.Credit)
            .Sum(l => l.Amount);

        if (debit <= 0 || credit <= 0)
            throw new GeneralJournalEntryDebitAndCreditLinesRequiredException(operation, documentId);

        if (debit != credit)
            throw new GeneralJournalEntryUnbalancedLinesException(operation, documentId, debit, credit);
    }

    private static void ValidateDraftHeader(
        string operation,
        Guid documentId,
        DocumentRecord doc,
        GeneralJournalEntryHeaderRecord header)
    {
        if (header is { AutoReverse: true, AutoReverseOnUtc: null })
            throw new GeneralJournalEntryAutoReverseOnUtcRequiredException(operation, documentId);

        if (header.AutoReverseOnUtc is not null)
        {
            var docDay = DateOnly.FromDateTime(doc.DateUtc);
            if (header.AutoReverseOnUtc.Value <= docDay)
                throw new GeneralJournalEntryAutoReverseOnUtcMustBeAfterDocumentDateException(operation, documentId, documentDayUtc: docDay, autoReverseOnUtc: header.AutoReverseOnUtc.Value);
        }
    }
    
    private async Task<IReadOnlyList<GeneralJournalEntryLineRecord>> NormalizeAndResolveLinesAsync(
        string operation,
        Guid documentId,
        IReadOnlyList<GeneralJournalEntryDraftLineInput> lines,
        CancellationToken ct)
    {
        if (lines.Count > 500)
            throw new GeneralJournalEntryLineCountLimitExceededException(operation, documentId, attemptedCount: lines.Count, maxAllowed: 500);

        var chart = await coaProvider.GetAsync(ct);

        var prepared = new List<(GeneralJournalEntryDraftLineInput Input, int LineNo, DimensionBag Bag)>(lines.Count);
        var lineNo = 1;

        foreach (var l in lines)
        {
            if (l.Amount <= 0)
                throw new GeneralJournalEntryLineAmountMustBePositiveException(operation, documentId, lineNo: lineNo, amount: l.Amount);

            var account = chart.Get(l.AccountId);

            var bag = BuildDimensionBagOrThrow(operation, documentId, lineNo, account, l);

            prepared.Add((l, lineNo, bag));
            lineNo++;
        }

        var setIds = await dimensionSets.GetOrCreateIdsAsync(prepared.Select(x => x.Bag).ToArray(), ct);
        var result = new List<GeneralJournalEntryLineRecord>(prepared.Count);

        for (var i = 0; i < prepared.Count; i++)
        {
            var line = prepared[i];
            result.Add(new GeneralJournalEntryLineRecord(
                documentId,
                line.LineNo,
                line.Input.Side,
                line.Input.AccountId,
                line.Input.Amount,
                line.Input.Memo,
                setIds[i]));
        }

        return result;
    }
    
    private static DimensionBag BuildDimensionBagOrThrow(
        string operation,
        Guid documentId,
        int lineNo,
        Account account,
        GeneralJournalEntryDraftLineInput input)
    {
        var hasInputDims = input.Dimensions is not null && input.Dimensions.Count > 0;

        // No dimension rules => Dimensions are not allowed.
        if (account.DimensionRules.Count == 0)
        {
            if (hasInputDims)
            {
                throw new GeneralJournalEntryLineDimensionsValidationException(
                    operation,
                    documentId,
                    lineNo,
                    input.AccountId,
                    account.Code,
                    GeneralJournalEntryLineDimensionsValidationException.ReasonDimensionsNotAllowed,
                    details: new Dictionary<string, object?>
                    {
                        ["attemptedDimensionCount"] = input.Dimensions!.Count,
                        ["attemptedDimensionIds"] = input.Dimensions!.Select(x => x.DimensionId).ToArray(),
                    });
            }

            return DimensionBag.Empty;
        }

        var map = new Dictionary<Guid, Guid>();

        if (hasInputDims)
        {
            foreach (var x in input.Dimensions!)
            {
                if (map.TryGetValue(x.DimensionId, out var existing) && existing != x.ValueId)
                {
                    throw new GeneralJournalEntryLineDimensionsValidationException(
                        operation,
                        documentId,
                        lineNo,
                        input.AccountId,
                        account.Code,
                        GeneralJournalEntryLineDimensionsValidationException.ReasonConflictingValues,
                        details: new Dictionary<string, object?>
                        {
                            ["dimensionId"] = x.DimensionId,
                            ["existingValueId"] = existing,
                            ["attemptedValueId"] = x.ValueId,
                        });
                }

                map[x.DimensionId] = x.ValueId;
            }
        }

        // Strict mode: no unknown dimensions.
        var allowed = account.DimensionRules.Select(r => r.DimensionId).ToHashSet();
        var unknown = map.Keys.Where(id => !allowed.Contains(id)).ToArray();
        if (unknown.Length > 0)
        {
            throw new GeneralJournalEntryLineDimensionsValidationException(
                operation,
                documentId,
                lineNo,
                input.AccountId,
                account.Code,
                GeneralJournalEntryLineDimensionsValidationException.ReasonUnknownDimensions,
                details: new Dictionary<string, object?>
                {
                    ["unknownDimensionIds"] = unknown,
                    ["allowedDimensionIds"] = allowed.ToArray(),
                });
        }

        // Required dimensions must be present.
        var missingRequired = account.DimensionRules
            .Where(r => r.IsRequired)
            .Where(r => !map.ContainsKey(r.DimensionId))
            .Select(r => (r.DimensionId, r.DimensionCode))
            .ToArray();

        if (missingRequired.Length > 0)
        {
            throw new GeneralJournalEntryLineDimensionsValidationException(
                operation,
                documentId,
                lineNo,
                input.AccountId,
                account.Code,
                GeneralJournalEntryLineDimensionsValidationException.ReasonMissingRequiredDimensions,
                details: new Dictionary<string, object?>
                {
                    ["missingDimensionIds"] = missingRequired.Select(x => x.DimensionId).ToArray(),
                    ["missingDimensionCodes"] = missingRequired.Select(x => x.DimensionCode).ToArray(),
                });
        }

        return map.Count == 0
            ? DimensionBag.Empty
            : new DimensionBag(map.Select(kvp => new DimensionValue(kvp.Key, kvp.Value)));
    }
    
    private async Task ValidateLinesAgainstChartOfAccountsAsync(
        string operation,
        Guid documentId,
        IReadOnlyList<GeneralJournalEntryLineRecord> lines,
        CancellationToken ct)
    {
        var chart = await coaProvider.GetAsync(ct);

        var setIds = lines
            .Select(x => x.DimensionSetId)
            .Distinct()
            .ToArray();

        var bagsById = await dimensionSetReader.GetBagsByIdsAsync(setIds, ct);

        foreach (var line in lines)
        {
            var account = chart.Get(line.AccountId);
            var bag = bagsById.TryGetValue(line.DimensionSetId, out var b) ? b : DimensionBag.Empty;

            if (account.DimensionRules.Count == 0)
            {
                if (!bag.IsEmpty)
                {
                    throw new GeneralJournalEntryLineDimensionsValidationException(
                        operation,
                        documentId,
                        line.LineNo,
                        line.AccountId,
                        account.Code,
                        GeneralJournalEntryLineDimensionsValidationException.ReasonDimensionsNotAllowed,
                        details: new Dictionary<string, object?>
                        {
                            ["dimensionSetId"] = line.DimensionSetId,
                            ["attemptedDimensionIds"] = bag.Items.Select(x => x.DimensionId).ToArray(),
                        });
                }

                continue;
            }

            var allowed = account.DimensionRules
                .Select(r => r.DimensionId)
                .ToHashSet();
            
            var unknown = bag.Items
                .Select(x => x.DimensionId)
                .Where(id => !allowed.Contains(id))
                .ToArray();
            
            if (unknown.Length > 0)
            {
                throw new GeneralJournalEntryLineDimensionsValidationException(
                    operation,
                    documentId,
                    line.LineNo,
                    line.AccountId,
                    account.Code,
                    GeneralJournalEntryLineDimensionsValidationException.ReasonUnknownDimensions,
                    details: new Dictionary<string, object?>
                    {
                        ["dimensionSetId"] = line.DimensionSetId,
                        ["unknownDimensionIds"] = unknown,
                        ["allowedDimensionIds"] = allowed.ToArray(),
                    });
            }

            var missingRequired = account.DimensionRules
                .Where(r => r.IsRequired)
                .Where(r => bag.Items.All(x => x.DimensionId != r.DimensionId))
                .Select(r => (r.DimensionId, r.DimensionCode))
                .ToArray();

            if (missingRequired.Length > 0)
            {
                throw new GeneralJournalEntryLineDimensionsValidationException(
                    operation,
                    documentId,
                    line.LineNo,
                    line.AccountId,
                    account.Code,
                    GeneralJournalEntryLineDimensionsValidationException.ReasonMissingRequiredDimensions,
                    details: new Dictionary<string, object?>
                    {
                        ["dimensionSetId"] = line.DimensionSetId,
                        ["missingDimensionIds"] = missingRequired.Select(x => x.DimensionId).ToArray(),
                        ["missingDimensionCodes"] = missingRequired.Select(x => x.DimensionCode).ToArray(),
                    });
            }
        }
    }
    
    private static IReadOnlyList<GeneralJournalEntryAllocationRecord> BuildAllocations(
        string operation,
        Guid documentId,
        IReadOnlyList<GeneralJournalEntryLineRecord> lines)
    {
        var debits = lines
            .Where(l => l.Side == GeneralJournalEntryModels.LineSide.Debit)
            .Select(l => (lineNo: l.LineNo, remaining: l.Amount))
            .ToList();

        var credits = lines
            .Where(l => l.Side == GeneralJournalEntryModels.LineSide.Credit)
            .Select(l => (lineNo: l.LineNo, remaining: l.Amount))
            .ToList();

        if (debits.Count == 0 || credits.Count == 0)
            throw new GeneralJournalEntryDebitAndCreditLinesRequiredException(operation, documentId);

        var allocations = new List<GeneralJournalEntryAllocationRecord>();
        var creditIdx = 0;
        var entryNo = 1;

        for (var debitIdx = 0; debitIdx < debits.Count; debitIdx++)
        {
            var (debitLineNo, debitRemaining) = debits[debitIdx];

            while (debitRemaining > 0)
            {
                if (creditIdx >= credits.Count)
                    throw new GeneralJournalEntryAllocationInvariantViolationException(operation, documentId, reason: "credits_exhausted");

                var (creditLineNo, creditRemaining) = credits[creditIdx];
                var amount = Math.Min(debitRemaining, creditRemaining);

                allocations.Add(new GeneralJournalEntryAllocationRecord(
                    documentId,
                    entryNo++,
                    debitLineNo,
                    creditLineNo,
                    amount)
                );

                debitRemaining -= amount;
                creditRemaining -= amount;

                credits[creditIdx] = (creditLineNo, creditRemaining);

                if (creditRemaining == 0)
                    creditIdx++;
            }
        }

        // Ensure fully consumed
        if (credits.Skip(creditIdx).Any(c => c.remaining > 0))
            throw new GeneralJournalEntryAllocationInvariantViolationException(operation, documentId, reason: "credit_remainder");

        return allocations;
    }
    
    private async Task<(DocumentRecord doc, GeneralJournalEntryHeaderRecord header)> LoadDraftForUpdateAsync(
        string operation,
        Guid documentId,
        CancellationToken ct)
    {
        var doc = await documents.GetForUpdateAsync(documentId, ct)
                  ?? throw new DocumentNotFoundException(documentId);

        if (!string.Equals(doc.TypeCode, AccountingDocumentTypeCodes.GeneralJournalEntry, StringComparison.Ordinal))
            throw new DocumentTypeMismatchException(documentId, expectedTypeCode: AccountingDocumentTypeCodes.GeneralJournalEntry, actualTypeCode: doc.TypeCode);

        if (doc.MarkedForDeletionAtUtc is not null || doc.Status == DocumentStatus.MarkedForDeletion)
        {
            var markedAt = doc.MarkedForDeletionAtUtc ?? doc.UpdatedAtUtc;
            throw new DocumentMarkedForDeletionException(operation, documentId, markedAt);
        }

        if (doc.Status != DocumentStatus.Draft)
            throw new DocumentWorkflowStateMismatchException(
                operation: operation,
                documentId: documentId,
                expectedState: nameof(DocumentStatus.Draft),
                actualState: doc.Status.ToString());

        var header = await EnsureHeaderForUpdateAsync(operation, documentId, ct);
        if (header.PostedAtUtc is not null)
            throw new DocumentWorkflowStateMismatchException(
                operation: operation,
                documentId: documentId,
                expectedState: "NotPosted",
                actualState: "Posted");

        return (doc, header);
    }
    
    private async Task<GeneralJournalEntryHeaderRecord> EnsureHeaderForUpdateAsync(
        string operation,
        Guid documentId,
        CancellationToken ct)
    {
        var header = await gje.GetHeaderForUpdateAsync(documentId, ct);
        if (header is null)
            throw new GeneralJournalEntryTypedHeaderNotFoundException(operation, documentId);

        return header;
    }

    private async Task TouchDocumentUpdatedAtAsync(DocumentRecord doc, DateTime nowUtc, CancellationToken ct)
    {
        // IDocumentRepository doesn't currently have a dedicated Touch method; UpdateStatus does update updated_at.
        await documents.UpdateStatusAsync(doc.Id, doc.Status, nowUtc, doc.PostedAtUtc, doc.MarkedForDeletionAtUtc, ct);
    }
}
