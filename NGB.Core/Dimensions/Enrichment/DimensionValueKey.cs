namespace NGB.Core.Dimensions.Enrichment;

/// <summary>
/// A (DimensionId, ValueId) pair used for enrichment.
/// </summary>
public readonly record struct DimensionValueKey(Guid DimensionId, Guid ValueId);
