namespace NGB.Runtime.Reporting.Planning;

public sealed record ReportPlanFieldSelection(
    string FieldCode,
    string OutputCode,
    string Label,
    string DataType);
