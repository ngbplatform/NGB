using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.Payables;
using NGB.PropertyManagement.Runtime.Payables;

namespace NGB.PropertyManagement.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/payables")]
public sealed class PayablesController : ControllerBase
{
    [HttpGet("open-items")]
    public Task<PayablesOpenItemsDetailsResponse> GetOpenItems(
        [FromServices] IPayablesOpenItemsDetailsService service,
        [FromQuery] Guid partyId,
        [FromQuery] Guid propertyId,
        [FromQuery] DateOnly? asOfMonth,
        [FromQuery] DateOnly? toMonth,
        CancellationToken ct)
        => service.GetOpenItemsDetailsAsync(partyId, propertyId, asOfMonth, toMonth, ct);

    [HttpGet("open-items/details")]
    public Task<PayablesOpenItemsDetailsResponse> GetOpenItemsDetails(
        [FromServices] IPayablesOpenItemsDetailsService service,
        [FromQuery] Guid partyId,
        [FromQuery] Guid propertyId,
        [FromQuery] DateOnly? asOfMonth,
        [FromQuery] DateOnly? toMonth,
        CancellationToken ct)
        => service.GetOpenItemsDetailsAsync(partyId, propertyId, asOfMonth, toMonth, ct);

    [HttpPost("apply/fifo/suggest")]
    public Task<PayablesSuggestFifoApplyResponse> SuggestFifoApply(
        [FromServices] IPayablesFifoApplySuggestService service,
        [FromBody] PayablesSuggestFifoApplyRequest request,
        CancellationToken ct)
        => service.SuggestAsync(request, ct);

    [HttpPost("apply/batch")]
    public Task<PayablesApplyBatchResponse> ApplyBatch(
        [FromServices] IPayablesApplyBatchService service,
        [FromBody] PayablesApplyBatchRequest request,
        CancellationToken ct)
        => service.ExecuteAsync(request, ct);

    [HttpPost("apply/{applyId:guid}/unapply")]
    public Task<PayablesUnapplyResponse> Unapply(
        [FromServices] IPayablesUnapplyService service,
        [FromRoute] Guid applyId,
        CancellationToken ct)
        => service.ExecuteAsync(applyId, ct);

    [HttpGet("reconciliation")]
    public Task<PayablesReconciliationReport> GetReconciliation(
        [FromServices] IPayablesReconciliationService service,
        [FromQuery] DateOnly fromMonthInclusive,
        [FromQuery] DateOnly toMonthInclusive,
        [FromQuery] PayablesReconciliationMode? mode,
        CancellationToken ct)
        => service.GetAsync(
            new PayablesReconciliationRequest(
                fromMonthInclusive,
                toMonthInclusive,
                mode ?? PayablesReconciliationMode.Movement),
            ct);
}
