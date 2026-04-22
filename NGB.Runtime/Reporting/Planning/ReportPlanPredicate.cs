using NGB.Contracts.Reporting;

namespace NGB.Runtime.Reporting.Planning;

public sealed record ReportPlanPredicate(
    string FieldCode,
    string OutputCode,
    string Label,
    string DataType,
    ReportFilterValueDto Filter);
