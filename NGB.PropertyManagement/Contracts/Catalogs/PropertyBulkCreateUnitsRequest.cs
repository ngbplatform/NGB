namespace NGB.PropertyManagement.Contracts.Catalogs;

/// <summary>
/// Bulk creation request for Unit properties under a Building (pm.property).
/// </summary>
public sealed class PropertyBulkCreateUnitsRequest
{
    public Guid BuildingId { get; init; }

    /// <summary>
    /// Unit number range start (inclusive).
    /// </summary>
    public int FromInclusive { get; init; }

    /// <summary>
    /// Unit number range end (inclusive).
    /// </summary>
    public int ToInclusive { get; init; }

    /// <summary>
    /// Step for the numeric sequence (default 1).
    /// </summary>
    public int Step { get; init; } = 1;

    /// <summary>
    /// .NET composite format string that produces unit_no.
    /// Supports:
    /// - {0} = number
    /// - {1} = floor (optional; computed when FloorSize is provided)
    /// Examples: "{0}", "{0:0000}", "{1}-{0:000}".
    /// </summary>
    public string UnitNoFormat { get; init; } = "{0}";

    /// <summary>
    /// Optional floor grouping size (e.g. 100 => 1..100 => floor 1, 101..200 => floor 2).
    /// When set, floor is available as {1} in UnitNoFormat.
    /// </summary>
    public int? FloorSize { get; init; }
}
