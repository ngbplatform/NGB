using NGB.Contracts.Reporting;
using Planning = NGB.Runtime.Reporting.Planning;

namespace NGB.Runtime.Reporting.Rendering;

internal static class ReportRowHierarchy
{
    public const string ColumnCode = "__row_hierarchy";

    public static bool HasHierarchy(IReadOnlyList<Planning.ReportPlanGrouping> rowGroups) => rowGroups.Count > 0;

    public static ReportSheetColumnDto CreateColumn(IReadOnlyList<Planning.ReportPlanGrouping> rowGroups)
        => new(
            Code: ColumnCode,
            Title: BuildTitle(rowGroups),
            DataType: "string",
            SemanticRole: "row-group");

    public static IReadOnlyList<string> BuildValueCodes(ReportQueryPlan plan)
    {
        var codes = new List<string>(plan.RowGroups.Count + plan.DetailFields.Count);
        codes.AddRange(plan.RowGroups.Select(x => x.OutputCode));
        codes.AddRange(plan.DetailFields.Select(x => x.OutputCode));
        return codes;
    }

    public static bool IsHierarchyColumn(ReportSheetColumnDto column)
        => string.Equals(column.Code, ColumnCode, StringComparison.OrdinalIgnoreCase);

    public static string BuildTitle(IReadOnlyList<Planning.ReportPlanGrouping> rowGroups)
        => rowGroups.Count == 0
            ? "Rows"
            : string.Join("\n", rowGroups.Select(x => x.Label));

    public static string FormatGroupLabel(
        ReportCellFormatter formatter,
        Planning.ReportPlanGrouping grouping,
        object? value)
        => formatter.FormatGroupLabel(value, grouping.TimeGrain);

    public static string FormatLeafLabel(
        ReportCellFormatter formatter,
        IReadOnlyList<Planning.ReportPlanGrouping> rowGroups,
        IReadOnlyList<object?> rowGroupValues)
    {
        if (rowGroups.Count == 0 || rowGroupValues.Count == 0)
            return string.Empty;

        var level = Math.Min(rowGroups.Count, rowGroupValues.Count) - 1;
        return formatter.FormatGroupLabel(rowGroupValues[level], rowGroups[level].TimeGrain);
    }
}
