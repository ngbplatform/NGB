namespace NGB.Runtime.Reporting.Planning;

public sealed record ReportPlanShape(
    bool ShowDetails,
    bool ShowSubtotals,
    bool ShowSubtotalsOnSeparateRows,
    bool ShowGrandTotals,
    bool IsPivot);
