using NGB.PropertyManagement.Contracts.Payables;

namespace NGB.PropertyManagement.Runtime.Payables;

public interface IPayablesUnapplyService
{
    Task<PayablesUnapplyResponse> ExecuteAsync(Guid applyId, CancellationToken ct = default);
}
