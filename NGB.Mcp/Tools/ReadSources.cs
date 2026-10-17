using NGB.Contracts.Reporting;

namespace NGB.Mcp.Tools;

internal static class ReadSources
{
    internal static McpReadSource? Create(
        McpSourceKind kind,
        string typeCode,
        Guid? id,
        string? title,
        string? url = null)
        => string.IsNullOrWhiteSpace(title) ? null : new(kind, typeCode, id, title, url);

    internal static IReadOnlyList<McpReadSource> Report(ReportDefinitionDto? report)
        => report is null ? [] : [new(McpSourceKind.Report, report.ReportCode, null, report.Name)];
}
