using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Receivables;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.Core.Security;
using NGB.PropertyManagement.Definitions;
using NGB.Runtime.Security;

namespace NGB.PropertyManagement.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/receivables")]
public sealed class ReceivablesController(INgbAccessChecker access) : ControllerBase
{
    [HttpGet("open-items")]
    public async Task<ReceivablesOpenItemsDetailsResponse> GetOpenItems(
        [FromServices] IReceivablesOpenItemsDetailsService service,
        [FromQuery] Guid leaseId,
        [FromQuery] Guid? partyId,
        [FromQuery] Guid? propertyId,
        [FromQuery] DateOnly? asOfMonth,
        [FromQuery] DateOnly? toMonth,
        CancellationToken ct)
    {
        await RequirePageAsync(PropertyManagementSecurityDefaults.ReceivablesOpenItemsPage, ct);
        return await service.GetOpenItemsDetailsAsync(
            partyId ?? Guid.Empty,
            propertyId ?? Guid.Empty,
            leaseId,
            asOfMonth,
            toMonth,
            ct);
    }

    [HttpGet("open-items/summary")]
    public async Task<ReceivablesOpenItemsResponse> GetOpenItemsSummary(
        [FromServices] IReceivablesOpenItemsService service,
        [FromQuery] Guid leaseId,
        [FromQuery] Guid? partyId,
        [FromQuery] Guid? propertyId,
        CancellationToken ct)
    {
        await RequirePageAsync(PropertyManagementSecurityDefaults.ReceivablesOpenItemsPage, ct);
        return await service.GetOpenItemsAsync(partyId ?? Guid.Empty, propertyId ?? Guid.Empty, leaseId, ct);
    }

    [HttpGet("open-items/details")]
    public async Task<ReceivablesOpenItemsDetailsResponse> GetOpenItemsDetails(
        [FromServices] IReceivablesOpenItemsDetailsService service,
        [FromQuery] Guid leaseId,
        [FromQuery] Guid? partyId,
        [FromQuery] Guid? propertyId,
        [FromQuery] DateOnly? asOfMonth,
        [FromQuery] DateOnly? toMonth,
        CancellationToken ct)
    {
        await RequirePageAsync(PropertyManagementSecurityDefaults.ReceivablesOpenItemsPage, ct);
        return await service.GetOpenItemsDetailsAsync(
            partyId ?? Guid.Empty,
            propertyId ?? Guid.Empty,
            leaseId,
            asOfMonth,
            toMonth,
            ct);
    }

    [HttpPost("apply/fifo/suggest")]
    public async Task<ReceivablesFifoApplySuggestResponse> SuggestFifoApply(
        [FromServices] IReceivablesFifoApplySuggestService service,
        [FromBody] ReceivablesFifoApplySuggestRequest request,
        CancellationToken ct)
    {
        await RequireDocumentAsync(PropertyManagementCodes.ReceivableApply, NgbPermissionActions.Create, ct);
        return await service.SuggestAsync(request, ct);
    }

    [HttpPost("apply/fifo/suggest/lease")]
    public async Task<ReceivablesSuggestFifoApplyResponse> SuggestLeaseFifoApply(
        [FromServices] IReceivablesFifoApplySuggestService service,
        [FromBody] ReceivablesSuggestFifoApplyRequest request,
        CancellationToken ct)
    {
        await RequireDocumentAsync(PropertyManagementCodes.ReceivableApply, NgbPermissionActions.Create, ct);
        return await service.SuggestLeaseAsync(request, ct);
    }
    
    [HttpPost("apply/fifo/execute")]
    public async Task<ReceivablesFifoApplyExecuteResponse> ExecuteFifoApply(
        [FromServices] IReceivablesFifoApplyExecuteService service,
        [FromBody] ReceivablesFifoApplyExecuteRequest request,
        CancellationToken ct)
    {
        await RequireDocumentAsync(PropertyManagementCodes.ReceivableApply, NgbPermissionActions.Post, ct);
        return await service.ExecuteAsync(request, ct);
    }

    [HttpPost("apply/custom/execute")]
    public async Task<ReceivablesCustomApplyExecuteResponse> ExecuteCustomApply(
        [FromServices] IReceivablesCustomApplyExecuteService service,
        [FromBody] ReceivablesCustomApplyExecuteRequest request,
        CancellationToken ct)
    {
        await RequireDocumentAsync(PropertyManagementCodes.ReceivableApply, NgbPermissionActions.Post, ct);
        return await service.ExecuteAsync(request, ct);
    }

    [HttpPost("apply/batch")]
    public async Task<ReceivablesApplyBatchResponse> ApplyBatch(
        [FromServices] IReceivablesApplyBatchService service,
        [FromBody] ReceivablesApplyBatchRequest request,
        CancellationToken ct)
    {
        await RequireDocumentAsync(PropertyManagementCodes.ReceivableApply, NgbPermissionActions.Post, ct);
        return await service.ExecuteAsync(request, ct);
    }

    [HttpPost("apply/{applyId:guid}/unapply")]
    public async Task<ReceivablesUnapplyResponse> Unapply(
        [FromServices] IReceivablesUnapplyService service,
        [FromRoute] Guid applyId,
        CancellationToken ct)
    {
        await RequireDocumentAsync(PropertyManagementCodes.ReceivableApply, NgbPermissionActions.Unpost, ct);
        return await service.ExecuteAsync(applyId, ct);
    }

    [HttpGet("reconciliation")]
    public async Task<ReceivablesReconciliationReport> GetReconciliation(
        [FromServices] IReceivablesReconciliationService service,
        [FromQuery] DateOnly fromMonthInclusive,
        [FromQuery] DateOnly toMonthInclusive,
        [FromQuery] ReceivablesReconciliationMode? mode,
        [FromQuery] int? offset,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        await RequirePageAsync(PropertyManagementSecurityDefaults.ReceivablesReconciliationPage, ct);
        return await service.GetAsync(
            new ReceivablesReconciliationRequest(
                fromMonthInclusive,
                toMonthInclusive,
                mode ?? ReceivablesReconciliationMode.Movement,
                offset ?? 0,
                limit ?? 200),
            ct);
    }

    private Task RequireDocumentAsync(string code, string action, CancellationToken ct)
        => access.RequireAsync(NgbResourceKinds.Document, code, action, ct);

    private Task RequirePageAsync(string code, CancellationToken ct)
        => access.RequireAsync(NgbResourceKinds.Page, code, NgbPermissionActions.View, ct);
}
