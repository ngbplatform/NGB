namespace NGB.PropertyManagement.Runtime.Payables;

public interface IPayablesOpenItemsService
{
    Task<(Guid RegisterId, IReadOnlyList<Contracts.Payables.PayablesOpenChargeItemDetailsDto> Charges, IReadOnlyList<Contracts.Payables.PayablesOpenCreditItemDetailsDto> Credits, decimal TotalOutstanding, decimal TotalCredit)> GetOpenItemsAsync(
        Guid partyId,
        Guid propertyId,
        DateOnly? asOfMonth = null,
        DateOnly? toMonth = null,
        CancellationToken ct = default);
}
