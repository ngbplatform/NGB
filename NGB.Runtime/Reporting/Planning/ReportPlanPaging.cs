namespace NGB.Runtime.Reporting.Planning;

public sealed record ReportPlanPaging(int Offset, int Limit, string? Cursor);
