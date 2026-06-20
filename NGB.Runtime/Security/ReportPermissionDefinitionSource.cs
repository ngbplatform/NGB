using NGB.Application.Abstractions.Services;
using NGB.Contracts.Security;
using NGB.Core.Security;

namespace NGB.Runtime.Security;

public sealed class ReportPermissionDefinitionSource(IReportDefinitionProvider reports)
    : INgbPermissionDefinitionSource
{
    private static readonly string[] ReportActions =
    [
        NgbPermissionActions.View,
        NgbPermissionActions.Execute,
        NgbPermissionActions.Export,
        NgbPermissionActions.SavePrivateVariant,
        NgbPermissionActions.ManageSharedVariants,
        NgbPermissionActions.DeleteVariant
    ];

    public async Task<IReadOnlyList<PermissionDefinitionDto>> GetDefinitionsAsync(CancellationToken ct)
    {
        var definitions = await reports.GetAllDefinitionsAsync(ct);
        return definitions
            .OrderBy(x => x.ReportCode, StringComparer.OrdinalIgnoreCase)
            .SelectMany(report => ReportActions.Select(action => new PermissionDefinitionDto(
                NgbResourceKinds.Report,
                report.ReportCode,
                action,
                $"{report.Name}: {Label(action)}",
                report.Group ?? "Reports")))
            .ToArray();
    }

    private static string Label(string action)
        => action
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static x => x.Length == 0 ? x : char.ToUpperInvariant(x[0]) + x[1..])
            .Aggregate((a, b) => $"{a} {b}");
}
