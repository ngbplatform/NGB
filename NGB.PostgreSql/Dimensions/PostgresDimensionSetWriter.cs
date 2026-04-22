using Dapper;
using NGB.Core.Dimensions;
using NGB.Persistence.Dimensions;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Dimensions;

/// <summary>
/// PostgreSQL implementation of <see cref="IDimensionSetWriter"/>.
///
/// Uses idempotent inserts (ON CONFLICT DO NOTHING) and participates in caller transaction.
///
/// Additionally, defends against a highly unlikely but dangerous corruption scenario where a
/// previously existing (dimension_set_id, dimension_id) row contains a different value_id than
/// the caller expects for the given deterministic DimensionSetId.
/// </summary>
public sealed class PostgresDimensionSetWriter(IUnitOfWork uow) : IDimensionSetWriter
{
    public async Task EnsureExistsAsync(
	    Guid dimensionSetId,
	    IReadOnlyList<DimensionValue> items,
	    CancellationToken ct = default)
    {
        if (dimensionSetId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(dimensionSetId), "DimensionSetId must not be empty.");

        if (items is null)
            throw new NgbArgumentRequiredException(nameof(items));

        if (items.Count == 0)
            throw new NgbArgumentInvalidException(nameof(items), "Items must not be empty for a non-empty DimensionSetId.");

        var normalizedItems = NormalizeSingleItems(items);
        await EnsureExistsBatchAsync([new DimensionSetWrite(dimensionSetId, normalizedItems)], ct);
    }

    public async Task EnsureExistsBatchAsync(IReadOnlyList<DimensionSetWrite> sets, CancellationToken ct = default)
    {
        if (sets is null)
            throw new NgbArgumentRequiredException(nameof(sets));

        if (sets.Count == 0)
            return;

        var normalizedSets = NormalizeSets(sets);

        // Dimension sets are part of the business transaction.
        // Autocommit would break atomicity and could produce orphan sets/items.
        await uow.EnsureOpenForTransactionAsync(ct);

        const string insertSetSql = """
                                     INSERT INTO platform_dimension_sets (dimension_set_id)
                                     SELECT DISTINCT x.dimension_set_id
                                     FROM UNNEST(@DimensionSetIds::uuid[]) AS x(dimension_set_id)
                                     ON CONFLICT (dimension_set_id) DO NOTHING;
                                     """;

        var insertSetCmd = new CommandDefinition(
            insertSetSql,
            new { DimensionSetIds = normalizedSets.Select(x => x.DimensionSetId).ToArray() },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(insertSetCmd);

        var totalItemCount = normalizedSets.Sum(x => x.Items.Count);
        var dimensionSetIds = new Guid[totalItemCount];
        var dimensionIds = new Guid[totalItemCount];
        var valueIds = new Guid[totalItemCount];

        var index = 0;
        foreach (var set in normalizedSets)
        {
            foreach (var (dimensionId, valueId) in set.Items)
            {
                dimensionSetIds[index] = set.DimensionSetId;
                dimensionIds[index] = dimensionId;
                valueIds[index] = valueId;
                index++;
            }
        }

        const string insertItemsSql = """
                                      INSERT INTO platform_dimension_set_items (dimension_set_id, dimension_id, value_id)
                                      SELECT x.dimension_set_id, x.dimension_id, x.value_id
                                      FROM UNNEST(@DimensionSetIds::uuid[], @DimensionIds::uuid[], @ValueIds::uuid[]) AS x(dimension_set_id, dimension_id, value_id)
                                      ON CONFLICT (dimension_set_id, dimension_id) DO NOTHING;
                                      """;

        var insertItemsCmd = new CommandDefinition(
            insertItemsSql,
            new { DimensionSetIds = dimensionSetIds, DimensionIds = dimensionIds, ValueIds = valueIds },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(insertItemsCmd);

        // Verify existing rows match expected values to prevent silent conflicts.
        const string selectExistingSql = """
                                          SELECT
                                              dimension_set_id AS DimensionSetId,
                                              dimension_id AS DimensionId,
                                              value_id AS ValueId
                                          FROM platform_dimension_set_items
                                          WHERE dimension_set_id = ANY(@DimensionSetIds::uuid[]);
                                          """;

        var selectExistingCmd = new CommandDefinition(
            selectExistingSql,
            new { DimensionSetIds = normalizedSets.Select(x => x.DimensionSetId).ToArray() },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = (await uow.Connection.QueryAsync<DimensionItemRow>(selectExistingCmd)).AsList();
        VerifyExistingRows(normalizedSets, rows);
    }

    private sealed class DimensionItemRow
    {
        public Guid DimensionSetId { get; init; }
        public Guid DimensionId { get; init; }
        public Guid ValueId { get; init; }
    }

    private static IReadOnlyList<NormalizedDimensionSet> NormalizeSets(IReadOnlyList<DimensionSetWrite> sets)
    {
        var normalized = new Dictionary<Guid, NormalizedDimensionSet>(capacity: sets.Count);

        for (var i = 0; i < sets.Count; i++)
        {
            var set = sets[i];

            if (set.DimensionSetId == Guid.Empty)
                throw new NgbArgumentInvalidException(nameof(sets), "DimensionSetId must not be empty.");

            if (set.Items is null)
                throw new NgbArgumentRequiredException($"{nameof(sets)}[{i}].{nameof(DimensionSetWrite.Items)}");

            if (set.Items.Count == 0)
                throw new NgbArgumentInvalidException(nameof(sets), "Items must not be empty for a non-empty DimensionSetId.");

            var expectedByDimensionId = new Dictionary<Guid, Guid>(capacity: set.Items.Count);

            for (var itemIndex = 0; itemIndex < set.Items.Count; itemIndex++)
            {
                var item = set.Items[itemIndex];

                if (expectedByDimensionId.TryGetValue(item.DimensionId, out var existingValueId))
                {
                    if (existingValueId != item.ValueId)
                    {
                        throw new NgbArgumentInvalidException(
                            nameof(sets),
                            $"Items must not contain duplicate DimensionId entries with different values. " +
                            $"DimensionId='{item.DimensionId}', ValueId1='{existingValueId}', ValueId2='{item.ValueId}'.");
                    }

                    continue;
                }

                expectedByDimensionId.Add(item.DimensionId, item.ValueId);
            }

            var candidate = new NormalizedDimensionSet(set.DimensionSetId, expectedByDimensionId);
            if (!normalized.TryAdd(candidate.DimensionSetId, candidate))
            {
                var existing = normalized[candidate.DimensionSetId];
                if (!AreEqual(existing.Items, candidate.Items))
                {
                    throw new NgbInvariantViolationException(
                        $"Dimension set '{candidate.DimensionSetId}' was provided with conflicting item bags in the same batch.",
                        context: new Dictionary<string, object?>
                        {
                            ["dimensionSetId"] = candidate.DimensionSetId
                        });
                }
            }
        }

        return normalized.Values.ToArray();
    }

    private static IReadOnlyList<DimensionValue> NormalizeSingleItems(IReadOnlyList<DimensionValue> items)
    {
        var expectedByDimensionId = new Dictionary<Guid, Guid>(capacity: items.Count);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];

            if (expectedByDimensionId.TryGetValue(item.DimensionId, out var existingValueId))
            {
                if (existingValueId != item.ValueId)
                {
                    throw new NgbArgumentInvalidException(
                        nameof(items),
                        $"Items must not contain duplicate DimensionId entries with different values. " +
                        $"DimensionId='{item.DimensionId}', ValueId1='{existingValueId}', ValueId2='{item.ValueId}'.");
                }

                continue;
            }

            expectedByDimensionId.Add(item.DimensionId, item.ValueId);
        }

        return expectedByDimensionId
            .Select(static x => new DimensionValue(x.Key, x.Value))
            .ToArray();
    }

    private static void VerifyExistingRows(
        IReadOnlyList<NormalizedDimensionSet> expectedSets,
        IReadOnlyList<DimensionItemRow> rows)
    {
        var rowsBySetId = rows
            .GroupBy(x => x.DimensionSetId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        foreach (var expectedSet in expectedSets)
        {
            if (!rowsBySetId.TryGetValue(expectedSet.DimensionSetId, out var actualRows))
            {
                throw new NgbInvariantViolationException(
                    $"Dimension set '{expectedSet.DimensionSetId}' items mismatch: expected {expectedSet.Items.Count} dimension(s), but found 0.",
                    context: new Dictionary<string, object?>
                    {
                        ["dimensionSetId"] = expectedSet.DimensionSetId,
                        ["expectedCount"] = expectedSet.Items.Count,
                        ["actualCount"] = 0
                    });
            }

            if (actualRows.Length != expectedSet.Items.Count)
            {
                throw new NgbInvariantViolationException(
                    $"Dimension set '{expectedSet.DimensionSetId}' items mismatch: expected {expectedSet.Items.Count} dimension(s), but found {actualRows.Length}.",
                    context: new Dictionary<string, object?>
                    {
                        ["dimensionSetId"] = expectedSet.DimensionSetId,
                        ["expectedCount"] = expectedSet.Items.Count,
                        ["actualCount"] = actualRows.Length
                    });
            }

            foreach (var row in actualRows)
            {
                if (!expectedSet.Items.TryGetValue(row.DimensionId, out var expectedValueId))
                {
                    throw new NgbInvariantViolationException(
                        $"Dimension set '{expectedSet.DimensionSetId}' contains unexpected dimension '{row.DimensionId}'.",
                        context: new Dictionary<string, object?>
                        {
                            ["dimensionSetId"] = expectedSet.DimensionSetId,
                            ["dimensionId"] = row.DimensionId
                        });
                }

                if (expectedValueId != row.ValueId)
                {
                    throw new NgbInvariantViolationException(
                        $"Dimension set '{expectedSet.DimensionSetId}' conflict for dimension '{row.DimensionId}': " +
                        $"expected value '{expectedValueId}', but found '{row.ValueId}'.",
                        context: new Dictionary<string, object?>
                        {
                            ["dimensionSetId"] = expectedSet.DimensionSetId,
                            ["dimensionId"] = row.DimensionId,
                            ["expectedValueId"] = expectedValueId,
                            ["actualValueId"] = row.ValueId
                        });
                }
            }
        }
    }

    private sealed record NormalizedDimensionSet(Guid DimensionSetId, IReadOnlyDictionary<Guid, Guid> Items);

    private static bool AreEqual(IReadOnlyDictionary<Guid, Guid> left, IReadOnlyDictionary<Guid, Guid> right)
    {
        if (left.Count != right.Count)
            return false;

        foreach (var (dimensionId, valueId) in left)
        {
            if (!right.TryGetValue(dimensionId, out var otherValueId) || otherValueId != valueId)
                return false;
        }

        return true;
    }
}
