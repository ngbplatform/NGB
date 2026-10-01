using NGB.Contracts.BusinessObjects;

namespace NGB.Application.Abstractions.BusinessObjects;

/// <summary>Resolves a supported identity and enforces access to its parent object.</summary>
public interface IBusinessObjectResolver
{
    Task<ResolvedBusinessObject> ResolveAsync(BusinessObjectRef target, CancellationToken ct);
}

public sealed record ResolvedBusinessObject(BusinessObjectRef Reference, string? Display);

public interface IBusinessObjectContentSummaryService
{
    Task<BusinessObjectContentSummary> GetAsync(BusinessObjectRef target, CancellationToken ct);
}
