using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;

namespace NGB.PropertyManagement.PostgreSql.Documents;

public interface IPropertyManagementPostingBatchHeadReader
{
    Task<IReadOnlyList<PmReceivableApplyHead>> ReadReceivableApplyHeadsAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<PmPayableApplyHead>> ReadPayableApplyHeadsAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default);
}

/// <summary>
/// Preloads typed apply heads for the production batch-apply paths. The data is read
/// through the current transaction and seeded into the operation-local posting cache,
/// replacing one typed-head query per apply document with one query per apply type.
/// </summary>
internal sealed class PropertyManagementPostingBatchReadPrefetcher(
    IPropertyManagementPostingBatchHeadReader readers,
    IDocumentPostingReadCache cache)
    : IDocumentPostingBatchReadPrefetcher
{
    public async Task PrefetchAsync(IReadOnlyList<DocumentRecord> documents, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var receivableApplyIds = SelectIds(documents, PropertyManagementCodes.ReceivableApply);
        if (receivableApplyIds.Length > 0)
        {
            var heads = await readers.ReadReceivableApplyHeadsAsync(receivableApplyIds, ct);
            foreach (var head in heads)
            {
                cache.Prime(
                    PostingCachedPropertyManagementDocumentReaders.CacheKey(
                        head.DocumentId,
                        nameof(IPropertyManagementDocumentReaders.ReadReceivableApplyHeadAsync)),
                    head);
            }
        }

        var payableApplyIds = SelectIds(documents, PropertyManagementCodes.PayableApply);
        if (payableApplyIds.Length > 0)
        {
            var heads = await readers.ReadPayableApplyHeadsAsync(payableApplyIds, ct);
            foreach (var head in heads)
            {
                cache.Prime(
                    PostingCachedPropertyManagementDocumentReaders.CacheKey(
                        head.DocumentId,
                        nameof(IPropertyManagementDocumentReaders.ReadPayableApplyHeadAsync)),
                    head);
            }
        }
    }

    private static Guid[] SelectIds(IReadOnlyList<DocumentRecord> documents, string typeCode)
        => documents
            .Where(document => string.Equals(document.TypeCode, typeCode, StringComparison.OrdinalIgnoreCase))
            .Select(static document => document.Id)
            .Distinct()
            .ToArray();
}
