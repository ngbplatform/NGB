using System.Text.Json;

namespace NGB.Contracts.Reporting;

public sealed record ReportFilterValueDto(JsonElement Value, bool IncludeDescendants = false);
