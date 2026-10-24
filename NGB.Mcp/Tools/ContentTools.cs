using NGB.Application.Abstractions.BusinessObjects;
using NGB.Application.Abstractions.Features;
using NGB.Application.Abstractions.Services;
using NGB.Attachments;
using NGB.Contracts.Attachments;
using NGB.Contracts.Audit;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Notes;
using NGB.Core.Features;
using NGB.Notes;

namespace NGB.Mcp.Tools;

internal sealed class NotesTool(INoteService notes, INgbFeatureService features)
    : IMcpReadToolHandler<ContentListRequest, BusinessObjectPage<NoteDto>>
{
    public Task<bool> IsAvailableAsync(CancellationToken ct) => features.IsEnabledAsync(NgbFeatures.Notes, ct);

    public Task<BusinessObjectPage<NoteDto>> ExecuteAsync(ContentListRequest request, CancellationToken ct)
        => notes.ListAsync(request.Target, request.Limit, request.Cursor, ct);
}

internal sealed class AttachmentsTool(IAttachmentService attachments, INgbFeatureService features)
    : IMcpReadToolHandler<ContentListRequest, BusinessObjectPage<AttachmentDto>>
{
    public Task<bool> IsAvailableAsync(CancellationToken ct)
        => features.IsEnabledAsync(NgbFeatures.Attachments, ct);

    public Task<BusinessObjectPage<AttachmentDto>> ExecuteAsync(ContentListRequest request, CancellationToken ct)
        => attachments.ListAsync(request.Target, request.Limit, request.Cursor, ct);
}

internal sealed class ContentSummaryTool(IBusinessObjectContentSummaryService summaries, INgbFeatureService features)
    : IMcpReadToolHandler<ContentSummaryRequest, BusinessObjectContentSummary>
{
    public async Task<bool> IsAvailableAsync(CancellationToken ct)
        => await features.IsEnabledAsync(NgbFeatures.Attachments, ct)
           || await features.IsEnabledAsync(NgbFeatures.Notes, ct);

    public Task<BusinessObjectContentSummary> ExecuteAsync(ContentSummaryRequest request, CancellationToken ct)
        => summaries.GetAsync(request.Target, ct);
}

internal sealed class AuditTool(IAuditLogQueryService audit) : IMcpReadToolHandler<AuditReadRequest, AuditLogPageDto>
{
    public Task<AuditLogPageDto> ExecuteAsync(AuditReadRequest request, CancellationToken ct)
    {
        ReadValidation.RequireId(request.Id);

        return audit.GetEntityAuditLogAsync(
            request.Kind,
            request.Id,
            request.AfterOccurredAtUtc,
            request.AfterAuditEventId,
            request.Limit,
            ct);
    }
}

internal sealed class WorkItemsTool(IWorkCenterQueryService work)
    : IMcpReadToolHandler<WorkItemsRequest, WorkItemsResult>
{
    public async Task<WorkItemsResult> ExecuteAsync(WorkItemsRequest request, CancellationToken ct)
    {
        var query = request.Query ?? new();
        if (query.Limit is < 1 or > 100)
            throw new McpReadException("invalid_input", "Work item page size must be between 1 and 100.");

        if (request.SummaryOnly)
            return new WorkItemsResult(Summary: await work.GetSummaryAsync(query.Vertical, ct));

        var page = await work.GetItemsAsync(query, ct);

        return new WorkItemsResult(Page: page with
        {
            Items = page.Items.Select(item => item with { PrimaryActionCode = null, Target = null }).ToArray()
        });
    }
}
