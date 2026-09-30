namespace NGB.Runtime.Reporting.Streaming;

/// <summary>
/// Retains source grouping keys separately from enriched presentation values.
/// A document label can change with the aggregation grain and must never identify a group.
/// </summary>
internal sealed record ReportStreamingDataRow(
    IReadOnlyDictionary<string, object?> RawValues,
    IReadOnlyDictionary<string, object?> Values);
