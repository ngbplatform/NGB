using NGB.Accounting.Accounts;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Accounting;
using NGB.Contracts.Metadata;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Storage;
using NGB.Persistence.Accounts;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Tools.Exceptions;
using NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;
using NGB.Runtime.Documents.Numbering;
using System.Globalization;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Documents.GeneralJournalEntry;

public sealed class GeneralJournalEntryUiService(
    IGeneralJournalEntryFacade facade,
    ICurrentActorContext currentActorContext,
    IDocumentRepository documentRepository,
    IGeneralJournalEntryRepository gje,
    IGeneralJournalEntryUiQueryRepository pageQuery,
    IDocumentPostingService posting,
    IUnitOfWork uow,
    IDocumentNumberingAndTypedSyncService numberingSync,
    IDimensionSetReader dimensionSets,
    IDimensionValueEnrichmentReader dimensionValueEnrichmentReader,
    IChartOfAccountsRepository chartOfAccounts,
    ICatalogTypeRegistry catalogs,
    IDocumentTypeRegistry documentTypes,
    TimeProvider timeProvider)
    : IGeneralJournalEntryUiService
{
    public async Task<GeneralJournalEntryPageDto> GetPageAsync(
        int offset,
        int limit,
        string? search,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? trash,
        CancellationToken ct)
    {
        // GJE is a platform document with a typed head table that intentionally does not expose a generic
        // `display` column. The universal document paging path requires such a column and therefore
        // cannot be used here without turning page reads into runtime 500s. Keep page reads on a dedicated,
        // typed read-model instead of routing through IDocumentService.GetPageAsync(...).
        var page = await pageQuery.GetPageAsync(offset, limit, search, dateFrom, dateTo, trash, ct);

        var items = page.Items
            .Select(x => new GeneralJournalEntryListItemDto(
                Id: x.Id,
                DateUtc: x.DateUtc,
                Number: x.Number,
                Display: x.Display,
                DocumentStatus: (int)x.DocumentStatus,
                IsMarkedForDeletion: x.IsMarkedForDeletion,
                JournalType: (int)x.JournalType,
                Source: (int)x.Source,
                ApprovalState: (int)x.ApprovalState,
                ReasonCode: x.ReasonCode,
                Memo: x.Memo,
                ExternalReference: x.ExternalReference,
                AutoReverse: x.AutoReverse,
                AutoReverseOnUtc: x.AutoReverseOnUtc,
                ReversalOfDocumentId: x.ReversalOfDocumentId,
                PostedBy: x.PostedBy,
                PostedAtUtc: x.PostedAtUtc))
            .ToArray();

        return new GeneralJournalEntryPageDto(items, page.Offset, page.Limit, page.Total);
    }

    public Task<GeneralJournalEntryDetailsDto> GetByIdAsync(Guid id, CancellationToken ct)
        => BuildDetailsAsync(id, ct);

    public async Task<GeneralJournalEntryDetailsDto> CreateDraftAsync(
        CreateGeneralJournalEntryDraftRequestDto request,
        CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        var id = await facade.CreateDraftAsync(request.DateUtc, ResolveCurrentActorDisplay(), ct);

        await uow.BeginTransactionAsync(ct);
        try
        {
            var locked = await documentRepository.GetForUpdateAsync(id, ct)
                         ?? throw new DocumentNotFoundException(id);

            if (string.IsNullOrWhiteSpace(locked.Number))
                await numberingSync.EnsureNumberAndSyncTypedAsync(locked, timeProvider.GetUtcNowDateTime(), ct);

            await uow.CommitAsync(ct);
        }
        catch
        {
            if (uow.HasActiveTransaction)
                await uow.RollbackAsync(ct);

            throw;
        }

        return await BuildDetailsAsync(id, ct);
    }

    public async Task<GeneralJournalEntryDetailsDto> UpdateHeaderAsync(
        Guid id,
        UpdateGeneralJournalEntryHeaderRequestDto request,
        CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        await facade.UpdateDraftHeaderAsync(
            id,
            new GeneralJournalEntryDraftHeaderUpdate(
                JournalType: request.JournalType is null
                    ? null
                    : (GeneralJournalEntryModels.JournalType)request.JournalType.Value,
                ReasonCode: request.ReasonCode,
                Memo: request.Memo,
                ExternalReference: request.ExternalReference,
                AutoReverse: request.AutoReverse,
                AutoReverseOnUtc: request.AutoReverseOnUtc),
            request.UpdatedBy,
            ct);

        return await BuildDetailsAsync(id, ct);
    }

    public async Task<GeneralJournalEntryDetailsDto> ReplaceLinesAsync(
        Guid id,
        ReplaceGeneralJournalEntryLinesRequestDto request,
        CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        var lines = request.Lines
            .Select(x => new GeneralJournalEntryDraftLineInput(
                Side: (GeneralJournalEntryModels.LineSide)x.Side,
                AccountId: x.AccountId,
                Amount: x.Amount,
                Memo: x.Memo,
                Dimensions: x.Dimensions?
                    .Select(d => new DimensionValue(d.DimensionId, d.ValueId))
                    .ToArray()))
            .ToArray();

        await facade.ReplaceDraftLinesAsync(id, lines, request.UpdatedBy, ct);
        return await BuildDetailsAsync(id, ct);
    }

    public async Task<GeneralJournalEntryDetailsDto> SubmitAsync(
        Guid id,
        CancellationToken ct)
    {
        await facade.SubmitAsync(id, ResolveCurrentActorDisplay(), ct);
        return await BuildDetailsAsync(id, ct);
    }

    public async Task<GeneralJournalEntryDetailsDto> ApproveAsync(
        Guid id,
        CancellationToken ct)
    {
        await facade.ApproveAsync(id, ResolveCurrentActorDisplay(), ct);
        return await BuildDetailsAsync(id, ct);
    }

    public async Task<GeneralJournalEntryDetailsDto> RejectAsync(
        Guid id,
        GeneralJournalEntryRejectRequestDto request,
        CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        await facade.RejectAsync(id, ResolveCurrentActorDisplay(), request.RejectReason, ct);
        return await BuildDetailsAsync(id, ct);
    }

    public async Task<GeneralJournalEntryDetailsDto> PostApprovedAsync(
        Guid id,
        CancellationToken ct)
    {
        await facade.PostApprovedAsync(id, ResolveCurrentActorDisplay(), ct);
        return await BuildDetailsAsync(id, ct);
    }

    public async Task<GeneralJournalEntryDetailsDto> ReversePostedAsync(
        Guid id,
        GeneralJournalEntryReverseRequestDto request,
        CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        var reversalId = await facade.ReversePostedAsync(
            id,
            request.ReversalDateUtc,
            ResolveCurrentActorDisplay(),
            request.PostImmediately,
            ct);
        
        return await BuildDetailsAsync(reversalId, ct);
    }

    private string ResolveCurrentActorDisplay()
    {
        var actor = currentActorContext.Current;
        if (actor is null)
            throw new GeneralJournalEntryCurrentActorRequiredException();

        var display = string.IsNullOrWhiteSpace(actor.DisplayName)
            ? string.IsNullOrWhiteSpace(actor.Email)
                ? actor.AuthSubject
                : actor.Email
            : actor.DisplayName;

        return display.Trim();
    }

    public async Task<GeneralJournalEntryDetailsDto> MarkForDeletionAsync(Guid id, CancellationToken ct)
    {
        await posting.MarkForDeletionAsync(id, ct);
        return await BuildDetailsAsync(id, ct);
    }

    public async Task<GeneralJournalEntryDetailsDto> UnmarkForDeletionAsync(Guid id, CancellationToken ct)
    {
        await posting.UnmarkForDeletionAsync(id, ct);
        return await BuildDetailsAsync(id, ct);
    }

    public async Task<GeneralJournalEntryAccountContextDto> GetAccountContextAsync(Guid accountId, CancellationToken ct)
    {
        var admin = await chartOfAccounts.GetAdminByIdAsync(accountId, ct)
                    ?? throw new AccountNotFoundException(accountId);
        return ToAccountContextDto(admin);
    }

    private LookupSourceDto? ResolveLookupSource(string dimensionCode)
    {
        if (catalogs.TryGet(dimensionCode, out _))
            return new CatalogLookupSourceDto(dimensionCode);

        if (documentTypes.TryGet(dimensionCode) is not null)
            return new DocumentLookupSourceDto([dimensionCode]);

        return null;
    }

    private static string BuildDisplay(DocumentRecord documentRecord)
    {
        var dateText = documentRecord.DateUtc.ToString("M/d/yyyy", CultureInfo.InvariantCulture);
        var number = documentRecord.Number?.Trim();

        return string.IsNullOrWhiteSpace(number)
            ? $"General Journal Entry {dateText}"
            : $"General Journal Entry {number} {dateText}";
    }

    private async Task<GeneralJournalEntryDetailsDto> BuildDetailsAsync(Guid id, CancellationToken ct)
    {
        var documentRecord = await documentRepository.GetAsync(id, ct) ?? throw new DocumentNotFoundException(id);
        var header = await gje.GetHeaderAsync(id, ct) ?? throw new DocumentNotFoundException(id);

        var lines = await gje.GetLinesAsync(id, ct);
        var allocations = await gje.GetAllocationsAsync(id, ct);
        var accountIds = lines.Select(x => x.AccountId).Distinct().ToArray();
        var accountAdmins = accountIds.Length == 0
            ? []
            : await chartOfAccounts.GetAdminByIdsAsync(accountIds, ct);
        var accountsById = accountAdmins.ToDictionary(x => x.Account.Id);

        var setIds = lines.Select(x => x.DimensionSetId).Distinct().ToArray();
        var bagsById = setIds.Length == 0
            ? new Dictionary<Guid, DimensionBag>()
            : await dimensionSets.GetBagsByIdsAsync(setIds, ct);
        var dimensionDisplayByKey = bagsById.Count == 0
            ? new Dictionary<DimensionValueKey, string>()
            : await dimensionValueEnrichmentReader.ResolveAsync(bagsById.Values.CollectValueKeys(), ct);
        var reversalOfDocumentDisplay = await ResolveReversalOfDisplayAsync(header.ReversalOfDocumentId, ct);
        var accountContexts = accountIds
            .Select(accountId => accountsById.TryGetValue(accountId, out var account)
                ? ToAccountContextDto(account)
                : null)
            .Where(x => x is not null)
            .Cast<GeneralJournalEntryAccountContextDto>()
            .ToArray();

        var document = new GeneralJournalEntryDocumentDto(
            Id: documentRecord.Id,
            Display: BuildDisplay(documentRecord),
            Status: (NGB.Contracts.Metadata.DocumentStatus)(int)documentRecord.Status,
            IsMarkedForDeletion: documentRecord.Status == Core.Documents.DocumentStatus.MarkedForDeletion,
            Number: documentRecord.Number);

        return new GeneralJournalEntryDetailsDto(
            Document: document,
            DateUtc: documentRecord.DateUtc,
            Header: new GeneralJournalEntryHeaderDto(
                JournalType: (int)header.JournalType,
                Source: (int)header.Source,
                ApprovalState: (int)header.ApprovalState,
                ReasonCode: header.ReasonCode,
                Memo: header.Memo,
                ExternalReference: header.ExternalReference,
                AutoReverse: header.AutoReverse,
                AutoReverseOnUtc: header.AutoReverseOnUtc,
                ReversalOfDocumentId: header.ReversalOfDocumentId,
                ReversalOfDocumentDisplay: reversalOfDocumentDisplay,
                InitiatedBy: header.InitiatedBy,
                InitiatedAtUtc: header.InitiatedAtUtc,
                SubmittedBy: header.SubmittedBy,
                SubmittedAtUtc: header.SubmittedAtUtc,
                ApprovedBy: header.ApprovedBy,
                ApprovedAtUtc: header.ApprovedAtUtc,
                RejectedBy: header.RejectedBy,
                RejectedAtUtc: header.RejectedAtUtc,
                RejectReason: header.RejectReason,
                PostedBy: header.PostedBy,
                PostedAtUtc: header.PostedAtUtc,
                CreatedAtUtc: header.CreatedAtUtc,
                UpdatedAtUtc: header.UpdatedAtUtc),
            Lines: lines
                .Select(line => new GeneralJournalEntryLineDto(
                    LineNo: line.LineNo,
                    Side: (int)line.Side,
                    AccountId: line.AccountId,
                    AccountDisplay: accountsById.TryGetValue(line.AccountId, out var account)
                        ? BuildAccountDisplay(account)
                        : line.AccountId.ToString(),
                    Amount: line.Amount,
                    Memo: line.Memo,
                    DimensionSetId: line.DimensionSetId,
                    Dimensions: (bagsById.TryGetValue(line.DimensionSetId, out var bag) ? bag : DimensionBag.Empty)
                        .Select(x =>
                        {
                            var key = new DimensionValueKey(x.DimensionId, x.ValueId);
                            var display = dimensionDisplayByKey.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                                ? value
                                : null;

                            return new GeneralJournalEntryDimensionValueDto(x.DimensionId, x.ValueId, display);
                        })
                        .ToArray()))
                .ToArray(),
            Allocations: allocations
                .Select(x => new GeneralJournalEntryAllocationDto(x.EntryNo, x.DebitLineNo, x.CreditLineNo, x.Amount))
                .ToArray(),
            AccountContexts: accountContexts);
    }

    private async Task<string?> ResolveReversalOfDisplayAsync(Guid? documentId, CancellationToken ct)
    {
        if (!documentId.HasValue || documentId == Guid.Empty)
            return null;

        var doc = await documentRepository.GetAsync(documentId.Value, ct);
        return doc is null ? null : BuildDisplay(doc);
    }

    private GeneralJournalEntryAccountContextDto ToAccountContextDto(ChartOfAccountsAdminItem admin)
    {
        var rules = admin.Account.DimensionRules
            .OrderBy(x => x.Ordinal)
            .Select(x => new GeneralJournalEntryDimensionRuleDto(
                DimensionId: x.DimensionId,
                DimensionCode: x.DimensionCode,
                Ordinal: x.Ordinal,
                IsRequired: x.IsRequired,
                Lookup: ResolveLookupSource(x.DimensionCode)))
            .ToArray();

        return new GeneralJournalEntryAccountContextDto(
            AccountId: admin.Account.Id,
            Code: admin.Account.Code,
            Name: admin.Account.Name,
            DimensionRules: rules);
    }

    private static string BuildAccountDisplay(ChartOfAccountsAdminItem admin)
        => string.IsNullOrWhiteSpace(admin.Account.Code)
            ? admin.Account.Name
            : $"{admin.Account.Code} — {admin.Account.Name}";
}
