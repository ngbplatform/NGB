using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.Payables;
using NGB.PropertyManagement.Runtime.Payables;
using NGB.Core.Security;
using NGB.PropertyManagement.Definitions;
using NGB.Runtime.Security;

namespace NGB.PropertyManagement.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/payables")]
public sealed class PayablesController(INgbAccessChecker access) : ControllerBase
{
    [HttpGet("open-items")]
    public async Task<PayablesOpenItemsDetailsResponse> GetOpenItems(
        [FromServices] IPayablesOpenItemsDetailsService service,
        [FromQuery] Guid partyId,
        [FromQuery] Guid propertyId,
        [FromQuery] DateOnly? asOfMonth,
        [FromQuery] DateOnly? toMonth,
        CancellationToken ct)
    {
        await RequirePageAsync(PropertyManagementSecurityDefaults.PayablesOpenItemsPage, ct);
        return await service.GetOpenItemsDetailsAsync(partyId, propertyId, asOfMonth, toMonth, ct);
    }

    [HttpGet("open-items/details")]
    public async Task<PayablesOpenItemsDetailsResponse> GetOpenItemsDetails(
        [FromServices] IPayablesOpenItemsDetailsService service,
        [FromQuery] Guid partyId,
        [FromQuery] Guid propertyId,
        [FromQuery] DateOnly? asOfMonth,
        [FromQuery] DateOnly? toMonth,
        CancellationToken ct)
    {
        await RequirePageAsync(PropertyManagementSecurityDefaults.PayablesOpenItemsPage, ct);
        return await service.GetOpenItemsDetailsAsync(partyId, propertyId, asOfMonth, toMonth, ct);
    }

    [HttpPost("apply/fifo/suggest")]
    public async Task<PayablesSuggestFifoApplyResponse> SuggestFifoApply(
        [FromServices] IPayablesFifoApplySuggestService service,
        [FromBody] PayablesSuggestFifoApplyRequest request,
        CancellationToken ct)
    {
        await RequireDocumentAsync(PropertyManagementCodes.PayableApply, NgbPermissionActions.Create, ct);
        return await service.SuggestAsync(request, ct);
    }

    [HttpPost("apply/batch")]
    public async Task<PayablesApplyBatchResponse> ApplyBatch(
        [FromServices] IPayablesApplyBatchService service,
        [FromBody] PayablesApplyBatchRequest request,
        CancellationToken ct)
    {
        await RequireDocumentAsync(PropertyManagementCodes.PayableApply, NgbPermissionActions.Post, ct);
        return await service.ExecuteAsync(request, ct);
    }

    [HttpPost("apply/{applyId:guid}/unapply")]
    public async Task<PayablesUnapplyResponse> Unapply(
        [FromServices] IPayablesUnapplyService service,
        [FromRoute] Guid applyId,
        CancellationToken ct)
    {
        await RequireDocumentAsync(PropertyManagementCodes.PayableApply, NgbPermissionActions.Unpost, ct);
        return await service.ExecuteAsync(applyId, ct);
    }

    [HttpGet("reconciliation")]
    public async Task<PayablesReconciliationReport> GetReconciliation(
        [FromServices] IPayablesReconciliationService service,
        [FromQuery] DateOnly fromMonthInclusive,
        [FromQuery] DateOnly toMonthInclusive,
        [FromQuery] PayablesReconciliationMode? mode,
        [FromQuery] int? offset,
        [FromQuery] int? limit,
        [FromQuery] string? cursor,
        CancellationToken ct)
    {
        await RequirePageAsync(PropertyManagementSecurityDefaults.PayablesReconciliationPage, ct);
        return await service.GetAsync(
            new PayablesReconciliationRequest(
                fromMonthInclusive,
                toMonthInclusive,
                mode ?? PayablesReconciliationMode.Movement,
                offset ?? 0,
                limit ?? 200,
                cursor),
            ct);
    }

    private Task RequireDocumentAsync(string code, string action, CancellationToken ct)
        => access.RequireAsync(NgbResourceKinds.Document, code, action, ct);

    private Task RequirePageAsync(string code, CancellationToken ct)
        => access.RequireAsync(NgbResourceKinds.Page, code, NgbPermissionActions.View, ct);
}
