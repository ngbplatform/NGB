using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Accounting;

namespace NGB.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/accounting/general-journal-entries")]
public sealed class GeneralJournalEntriesController(IGeneralJournalEntryUiService service) : ControllerBase
{
    [HttpGet]
    public Task<GeneralJournalEntryPageDto> GetPage(
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        [FromQuery] string? search = null,
        [FromQuery] DateOnly? dateFrom = null,
        [FromQuery] DateOnly? dateTo = null,
        [FromQuery] string? trash = null,
        CancellationToken ct = default)
        => service.GetPageAsync(offset, limit, search, dateFrom, dateTo, trash, ct);

    [HttpGet("{id:guid}")]
    public Task<GeneralJournalEntryDetailsDto> GetById([FromRoute] Guid id, CancellationToken ct)
        => service.GetByIdAsync(id, ct);

    [HttpPost]
    public Task<GeneralJournalEntryDetailsDto> CreateDraft(
        [FromBody] CreateGeneralJournalEntryDraftRequestDto request,
        CancellationToken ct)
        => service.CreateDraftAsync(request, ct);

    [HttpPut("{id:guid}/header")]
    public Task<GeneralJournalEntryDetailsDto> UpdateHeader(
        [FromRoute] Guid id,
        [FromBody] UpdateGeneralJournalEntryHeaderRequestDto request,
        CancellationToken ct)
        => service.UpdateHeaderAsync(id, request, ct);

    [HttpPut("{id:guid}/lines")]
    public Task<GeneralJournalEntryDetailsDto> ReplaceLines(
        [FromRoute] Guid id,
        [FromBody] ReplaceGeneralJournalEntryLinesRequestDto request,
        CancellationToken ct)
        => service.ReplaceLinesAsync(id, request, ct);

    [HttpPost("{id:guid}/submit")]
    public Task<GeneralJournalEntryDetailsDto> Submit([FromRoute] Guid id, CancellationToken ct)
        => service.SubmitAsync(id, ct);

    [HttpPost("{id:guid}/approve")]
    public Task<GeneralJournalEntryDetailsDto> Approve([FromRoute] Guid id, CancellationToken ct)
        => service.ApproveAsync(id, ct);

    [HttpPost("{id:guid}/reject")]
    public Task<GeneralJournalEntryDetailsDto> Reject(
        [FromRoute] Guid id,
        [FromBody] GeneralJournalEntryRejectRequestDto request,
        CancellationToken ct)
        => service.RejectAsync(id, request, ct);

    [HttpPost("{id:guid}/post")]
    public Task<GeneralJournalEntryDetailsDto> PostApproved([FromRoute] Guid id, CancellationToken ct)
        => service.PostApprovedAsync(id, ct);

    [HttpPost("{id:guid}/reverse")]
    public Task<GeneralJournalEntryDetailsDto> ReversePosted(
        [FromRoute] Guid id,
        [FromBody] GeneralJournalEntryReverseRequestDto request,
        CancellationToken ct)
        => service.ReversePostedAsync(id, request, ct);

    [HttpPost("{id:guid}/mark-for-deletion")]
    public Task<GeneralJournalEntryDetailsDto> MarkForDeletion([FromRoute] Guid id, CancellationToken ct)
        => service.MarkForDeletionAsync(id, ct);

    [HttpPost("{id:guid}/unmark-for-deletion")]
    public Task<GeneralJournalEntryDetailsDto> UnmarkForDeletion([FromRoute] Guid id, CancellationToken ct)
        => service.UnmarkForDeletionAsync(id, ct);

    [HttpGet("accounts/{accountId:guid}")]
    public Task<GeneralJournalEntryAccountContextDto> GetAccountContext([FromRoute] Guid accountId, CancellationToken ct)
        => service.GetAccountContextAsync(accountId, ct);
}
