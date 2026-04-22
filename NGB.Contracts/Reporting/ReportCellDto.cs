using System.Text.Json;

namespace NGB.Contracts.Reporting;

public sealed record ReportCellDto(
    JsonElement? Value = null,
    string? Display = null,
    string? ValueType = null,
    int ColSpan = 1,
    int RowSpan = 1,
    string? StyleKey = null,
    string? SemanticRole = null,
    ReportCellActionDto? Action = null);
