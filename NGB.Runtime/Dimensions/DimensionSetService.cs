using NGB.Core.Dimensions;
using NGB.Persistence.Dimensions;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Dimensions;

/// <summary>
/// Deterministically maps a <see cref="DimensionBag"/> to a stable DimensionSetId and persists the mapping.
/// </summary>
public sealed class DimensionSetService(IDimensionSetWriter writer) : IDimensionSetService
{
    public async Task<Guid> GetOrCreateIdAsync(DimensionBag bag, CancellationToken ct = default)
    {
        if (bag is null)
            throw new NgbArgumentRequiredException(nameof(bag));
        
        if (bag.IsEmpty)
            return Guid.Empty;

        var dimensionSetId = DeterministicDimensionSetId.FromBag(bag);

        // Persist mapping (Guid.Empty row is reserved and already exists).
        await writer.EnsureExistsAsync(dimensionSetId, bag.Items, ct);

        return dimensionSetId;
    }

    public async Task<IReadOnlyList<Guid>> GetOrCreateIdsAsync(
        IReadOnlyList<DimensionBag> bags,
        CancellationToken ct = default)
    {
        if (bags is null)
            throw new NgbArgumentRequiredException(nameof(bags));

        if (bags.Count == 0)
            return [];

        var result = new Guid[bags.Count];
        var writes = new Dictionary<Guid, DimensionSetWrite>(capacity: bags.Count);

        for (var i = 0; i < bags.Count; i++)
        {
            var bag = bags[i];
            if (bag is null)
                throw new NgbArgumentRequiredException($"{nameof(bags)}[{i}]");

            if (bag.IsEmpty)
            {
                result[i] = Guid.Empty;
                continue;
            }

            var dimensionSetId = DeterministicDimensionSetId.FromBag(bag);
            result[i] = dimensionSetId;

            writes.TryAdd(dimensionSetId, new DimensionSetWrite(dimensionSetId, bag.Items));
        }

        if (writes.Count > 0)
            await writer.EnsureExistsBatchAsync(writes.Values.ToArray(), ct);

        return result;
    }
}
