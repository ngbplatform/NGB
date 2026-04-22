using NGB.Core.Dimensions;
using NGB.ReferenceRegisters.Contracts;

namespace NGB.Runtime.ReferenceRegisters;

/// <summary>
/// A UI/reporting-friendly projection for reference register reads:
/// the raw record row + resolved dimensions + display strings.
/// </summary>
public sealed record ReferenceRegisterRecordSnapshot(
    ReferenceRegisterRecordRead Record,
    DimensionBag Dimensions,
    IReadOnlyDictionary<Guid, string> DimensionValueDisplaysByDimensionId);
