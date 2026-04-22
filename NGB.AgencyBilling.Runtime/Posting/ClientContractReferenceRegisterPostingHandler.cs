using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.ReferenceRegisters.Contracts;

namespace NGB.AgencyBilling.Runtime.Posting;

public sealed class ClientContractReferenceRegisterPostingHandler : IDocumentReferenceRegisterPostingHandler
{
    public string TypeCode => AgencyBillingCodes.ClientContract;

    public Task BuildRecordsAsync(
        DocumentRecord document,
        ReferenceRegisterWriteOperation operation,
        IReferenceRegisterRecordsBuilder builder,
        CancellationToken ct)
        => Task.CompletedTask;
}
