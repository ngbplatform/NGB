namespace NGB.Contracts.Reporting;

public sealed record ReportRunDto(Guid Id, string Status, int RowCount, DateTime ExpiresAtUtc);
