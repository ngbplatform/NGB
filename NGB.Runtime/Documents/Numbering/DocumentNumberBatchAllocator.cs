using NGB.Persistence.Documents.Numbering;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Documents.Numbering;

public sealed class DocumentNumberBatchAllocator(
    IDocumentNumberSequenceRepository sequences,
    IDocumentNumberFormatter formatter)
    : IDocumentNumberBatchAllocator
{
    public async Task<IReadOnlyDictionary<Guid, string>> AllocateAsync(
        IReadOnlyList<DocumentNumberAllocationRequest> requests,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        if (requests.Count == 0)
            return new Dictionary<Guid, string>();

        var duplicateId = requests
            .GroupBy(static request => request.DocumentId)
            .FirstOrDefault(static group => group.Count() > 1);

        if (duplicateId is not null)
        {
            throw new NgbArgumentInvalidException(
                nameof(requests),
                $"Document number allocation contains duplicate id '{duplicateId.Key}'.");
        }

        foreach (var request in requests)
        {
            request.DocumentId.EnsureRequired(nameof(requests));
            if (string.IsNullOrWhiteSpace(request.TypeCode))
                throw new NgbArgumentRequiredException(nameof(requests));

            request.DateUtc.EnsureUtc(nameof(requests));
        }

        var result = new Dictionary<Guid, string>(requests.Count);
        foreach (var group in requests.GroupBy(static request =>
                     new SequenceKey(request.TypeCode, request.DateUtc.Year)))
        {
            var items = group.ToArray();
            var first = sequences is IDocumentNumberSequenceBatchRepository batch
                ? await batch.ReserveAsync(group.Key.TypeCode, group.Key.FiscalYear, items.Length, ct)
                : await ReserveOneByOneAsync(group.Key, items.Length, ct);

            for (var index = 0; index < items.Length; index++)
            {
                result.Add(
                    items[index].DocumentId,
                    formatter.Format(group.Key.TypeCode, group.Key.FiscalYear, checked(first + index)));
            }
        }

        return result;
    }

    private async Task<long> ReserveOneByOneAsync(SequenceKey key, int count, CancellationToken ct)
    {
        var first = await sequences.NextAsync(key.TypeCode, key.FiscalYear, ct);
        for (var index = 1; index < count; index++)
        {
            var next = await sequences.NextAsync(key.TypeCode, key.FiscalYear, ct);
            if (next != checked(first + index))
            {
                throw new NgbInvariantViolationException(
                    $"Document sequence '{key.TypeCode}/{key.FiscalYear}' did not allocate a contiguous range.");
            }
        }

        return first;
    }

    private readonly record struct SequenceKey(string TypeCode, int FiscalYear);
}
