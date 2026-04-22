using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting.Planning;

namespace NGB.Runtime.Reporting;

public sealed record ReportQueryPlan(
    string ReportCode,
    string? DatasetCode,
    ReportExecutionMode Mode,
    IReadOnlyList<ReportPlanGrouping> RowGroups,
    IReadOnlyList<ReportPlanGrouping> ColumnGroups,
    IReadOnlyList<ReportPlanMeasure> Measures,
    IReadOnlyList<ReportPlanFieldSelection> DetailFields,
    IReadOnlyList<ReportPlanSort> Sorts,
    IReadOnlyList<ReportPlanPredicate> Predicates,
    IReadOnlyList<ReportPlanParameter> Parameters,
    ReportPlanShape Shape,
    ReportPlanPaging Paging);
