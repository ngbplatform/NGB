using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Receivables;
using NGB.PropertyManagement.Runtime.Receivables;

namespace NGB.PropertyManagement.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/receivables")]
public sealed class ReceivablesController : ControllerBase
{
    [HttpGet("open-items")]
    public Task<ReceivablesOpenItemsDetailsResponse> GetOpenItems(
        [FromServices] IReceivablesOpenItemsDetailsService service,
        [FromQuery] Guid leaseId,
        [FromQuery] Guid? partyId,
        [FromQuery] Guid? propertyId,
        [FromQuery] DateOnly? asOfMonth,
        [FromQuery] DateOnly? toMonth,
        CancellationToken ct)
        => service.GetOpenItemsDetailsAsync(
            partyId ?? Guid.Empty,
            propertyId ?? Guid.Empty,
            leaseId,
            asOfMonth,
            toMonth,
            ct);

    [HttpGet("open-items/summary")]
    public Task<ReceivablesOpenItemsResponse> GetOpenItemsSummary(
        [FromServices] IReceivablesOpenItemsService service,
        [FromQuery] Guid leaseId,
        [FromQuery] Guid? partyId,
        [FromQuery] Guid? propertyId,
        CancellationToken ct)
        => service.GetOpenItemsAsync(partyId ?? Guid.Empty, propertyId ?? Guid.Empty, leaseId, ct);

    [HttpGet("open-items/details")]
    public Task<ReceivablesOpenItemsDetailsResponse> GetOpenItemsDetails(
        [FromServices] IReceivablesOpenItemsDetailsService service,
        [FromQuery] Guid leaseId,
        [FromQuery] Guid? partyId,
        [FromQuery] Guid? propertyId,
        [FromQuery] DateOnly? asOfMonth,
        [FromQuery] DateOnly? toMonth,
        CancellationToken ct)
        => service.GetOpenItemsDetailsAsync(
            partyId ?? Guid.Empty,
            propertyId ?? Guid.Empty,
            leaseId,
            asOfMonth,
            toMonth,
            ct);

    [HttpPost("apply/fifo/suggest")]
    public Task<ReceivablesFifoApplySuggestResponse> SuggestFifoApply(
        [FromServices] IReceivablesFifoApplySuggestService service,
        [FromBody] ReceivablesFifoApplySuggestRequest request,
        CancellationToken ct)
        => service.SuggestAsync(request, ct);

    [HttpPost("apply/fifo/suggest/lease")]
    public Task<ReceivablesSuggestFifoApplyResponse> SuggestLeaseFifoApply(
        [FromServices] IReceivablesFifoApplySuggestService service,
        [FromBody] ReceivablesSuggestFifoApplyRequest request,
        CancellationToken ct)
        => service.SuggestLeaseAsync(request, ct);
    
    [HttpPost("apply/fifo/execute")]
    public Task<ReceivablesFifoApplyExecuteResponse> ExecuteFifoApply(
        [FromServices] IReceivablesFifoApplyExecuteService service,
        [FromBody] ReceivablesFifoApplyExecuteRequest request,
        CancellationToken ct)
        => service.ExecuteAsync(request, ct);

    [HttpPost("apply/custom/execute")]
    public Task<ReceivablesCustomApplyExecuteResponse> ExecuteCustomApply(
        [FromServices] IReceivablesCustomApplyExecuteService service,
        [FromBody] ReceivablesCustomApplyExecuteRequest request,
        CancellationToken ct)
        => service.ExecuteAsync(request, ct);

    [HttpPost("apply/batch")]
    public Task<ReceivablesApplyBatchResponse> ApplyBatch(
        [FromServices] IReceivablesApplyBatchService service,
        [FromBody] ReceivablesApplyBatchRequest request,
        CancellationToken ct)
        => service.ExecuteAsync(request, ct);

    [HttpPost("apply/{applyId:guid}/unapply")]
    public Task<ReceivablesUnapplyResponse> Unapply(
        [FromServices] IReceivablesUnapplyService service,
        [FromRoute] Guid applyId,
        CancellationToken ct)
        => service.ExecuteAsync(applyId, ct);

    [HttpGet("reconciliation")]
    public Task<ReceivablesReconciliationReport> GetReconciliation(
        [FromServices] IReceivablesReconciliationService service,
        [FromQuery] DateOnly fromMonthInclusive,
        [FromQuery] DateOnly toMonthInclusive,
        [FromQuery] ReceivablesReconciliationMode? mode,
        CancellationToken ct)
        => service.GetAsync(
            new ReceivablesReconciliationRequest(
                fromMonthInclusive,
                toMonthInclusive,
                mode ?? ReceivablesReconciliationMode.Movement),
            ct);
}
