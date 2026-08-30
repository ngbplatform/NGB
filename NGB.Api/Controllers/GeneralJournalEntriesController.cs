using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Accounting.Documents;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Accounting;
using NGB.Core.Security;
using NGB.Runtime.Security;

namespace NGB.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/accounting/general-journal-entries")]
public sealed class GeneralJournalEntriesController(IGeneralJournalEntryUiService service, INgbAccessChecker access)
    : ControllerBase
{
    [HttpGet]
    public async Task<GeneralJournalEntryPageDto> GetPage(
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        [FromQuery] string? search = null,
        [FromQuery] DateOnly? dateFrom = null,
        [FromQuery] DateOnly? dateTo = null,
        [FromQuery] string? trash = null,
        [FromQuery] string? cursor = null,
        CancellationToken ct = default)
    {
        await RequireAsync(NgbPermissionActions.View, ct);

        return string.IsNullOrWhiteSpace(cursor)
            ? await service.GetPageAsync(offset, limit, search, dateFrom, dateTo, trash, ct)
            : await service.GetCursorPageAsync(cursor, limit, search, dateFrom, dateTo, trash, ct);
    }

    [HttpGet("{id:guid}")]
    public async Task<GeneralJournalEntryDetailsDto> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        await RequireAsync(NgbPermissionActions.View, ct);
        return await service.GetByIdAsync(id, ct);
    }

    [HttpPost]
    public async Task<GeneralJournalEntryDetailsDto> CreateDraft(
        [FromBody] CreateGeneralJournalEntryDraftRequestDto request,
        CancellationToken ct)
    {
        await RequireAsync(NgbPermissionActions.Create, ct);
        return await service.CreateDraftAsync(request, ct);
    }

    [HttpPut("{id:guid}/header")]
    public async Task<GeneralJournalEntryDetailsDto> UpdateHeader(
        [FromRoute] Guid id,
        [FromBody] UpdateGeneralJournalEntryHeaderRequestDto request,
        CancellationToken ct)
    {
        await RequireAsync(NgbPermissionActions.EditDraft, ct);
        return await service.UpdateHeaderAsync(id, request, ct);
    }

    [HttpPut("{id:guid}/lines")]
    public async Task<GeneralJournalEntryDetailsDto> ReplaceLines(
        [FromRoute] Guid id,
        [FromBody] ReplaceGeneralJournalEntryLinesRequestDto request,
        CancellationToken ct)
    {
        await RequireAsync(NgbPermissionActions.EditDraft, ct);
        return await service.ReplaceLinesAsync(id, request, ct);
    }

    [HttpPost("{id:guid}/submit")]
    public async Task<GeneralJournalEntryDetailsDto> Submit([FromRoute] Guid id, CancellationToken ct)
    {
        await RequireAsync(NgbPermissionActions.EditDraft, ct);
        return await service.SubmitAsync(id, ct);
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<GeneralJournalEntryDetailsDto> Approve([FromRoute] Guid id, CancellationToken ct)
    {
        await RequireAsync(NgbPermissionActions.Post, ct);
        return await service.ApproveAsync(id, ct);
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<GeneralJournalEntryDetailsDto> Reject(
        [FromRoute] Guid id,
        [FromBody] GeneralJournalEntryRejectRequestDto request,
        CancellationToken ct)
    {
        await RequireAsync(NgbPermissionActions.Post, ct);
        return await service.RejectAsync(id, request, ct);
    }

    [HttpPost("{id:guid}/post")]
    public async Task<GeneralJournalEntryDetailsDto> PostApproved([FromRoute] Guid id, CancellationToken ct)
    {
        await RequireAsync(NgbPermissionActions.Post, ct);
        return await service.PostApprovedAsync(id, ct);
    }

    [HttpPost("{id:guid}/reverse")]
    public async Task<GeneralJournalEntryDetailsDto> ReversePosted(
        [FromRoute] Guid id,
        [FromBody] GeneralJournalEntryReverseRequestDto request,
        CancellationToken ct)
    {
        await RequireAsync(NgbPermissionActions.Unpost, ct);
        return await service.ReversePostedAsync(id, request, ct);
    }

    [HttpPost("{id:guid}/mark-for-deletion")]
    public async Task<GeneralJournalEntryDetailsDto> MarkForDeletion([FromRoute] Guid id, CancellationToken ct)
    {
        await RequireAsync(NgbPermissionActions.MarkForDeletion, ct);
        return await service.MarkForDeletionAsync(id, ct);
    }

    [HttpPost("{id:guid}/unmark-for-deletion")]
    public async Task<GeneralJournalEntryDetailsDto> UnmarkForDeletion([FromRoute] Guid id, CancellationToken ct)
    {
        await RequireAsync(NgbPermissionActions.UnmarkForDeletion, ct);
        return await service.UnmarkForDeletionAsync(id, ct);
    }

    [HttpGet("accounts/{accountId:guid}")]
    public async Task<GeneralJournalEntryAccountContextDto> GetAccountContext([FromRoute] Guid accountId, CancellationToken ct)
    {
        await RequireAsync(NgbPermissionActions.View, ct);
        return await service.GetAccountContextAsync(accountId, ct);
    }

    private Task RequireAsync(string action, CancellationToken ct)
        => access.RequireAsync(
            NgbResourceKinds.Document,
            AccountingDocumentTypeCodes.GeneralJournalEntry,
            action,
            ct);
}
