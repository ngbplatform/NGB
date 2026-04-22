using NGB.PropertyManagement.Contracts.Receivables;

namespace NGB.PropertyManagement.Runtime.Receivables;

public interface IReceivablesUnapplyService
{
    Task<ReceivablesUnapplyResponse> ExecuteAsync(Guid applyId, CancellationToken ct = default);
}
