namespace NGB.Persistence.Documents;

/// <summary>
/// Bulk document display resolver used by report/UI layers to avoid N+1 lookups.
/// </summary>
public sealed record DocumentDisplayRef(Guid Id, string TypeCode, string Display);

public interface IDocumentDisplayReader
{
    Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, DocumentDisplayRef>> ResolveRefsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct = default);
}
