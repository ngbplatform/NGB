using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting;
using NGB.Core.Security;
using NGB.Runtime.Security;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting;

public sealed class PermissionAwareReportReadService(
    IReportDefinitionProvider definitions,
    IReportEngine engine,
    INgbAccessChecker access,
    NgbSecurityCache cache)
    : IAuthorizedReportReadService
{
    public async Task<IReadOnlyList<ReportDefinitionDto>> GetAllDefinitionsAsync(CancellationToken ct)
    {
        var snapshot = await access.GetSnapshotAsync(ct);
        if (!snapshot.HasAny(NgbResourceKinds.Report, NgbPermissionActions.View)
            && !snapshot.HasAny(NgbResourceKinds.Report, NgbPermissionActions.Execute)
            && !HasAnyAdminBackedReportAccess(snapshot))
        {
            return [];
        }

        return (await cache.GetOrCreateReportDefinitionsAsync(
            snapshot,
            async token =>
            {
                var all = await definitions.GetAllDefinitionsAsync(token);
                return FilterDefinitions(all, snapshot);
            },
            ct))!;
    }

    public async Task<ReportDefinitionDto> GetDefinitionAsync(string reportCode, CancellationToken ct)
    {
        var snapshot = await access.GetSnapshotAsync(ct);
        RequireViewOrExecuteReport(snapshot, reportCode);
        return await definitions.GetDefinitionAsync(reportCode, ct);
    }

    public async Task<ReportExecutionResponseDto> ExecuteAsync(
        string reportCode,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var snapshot = await access.GetSnapshotAsync(ct);
        RequireExecuteReport(snapshot, reportCode);

        if (request.DisablePaging)
            throw new NgbArgumentInvalidException("disablePaging", "Interactive reports require paging. Use the export endpoint for a complete download.");

        if (request.Offset != 0)
            throw new NgbArgumentInvalidException("offset", "Use the continuation cursor to request another page.");

        var response = await engine.ExecuteAsync(reportCode, request, ct);
        var sheet = ScrubForbiddenCellActions(response.Sheet, snapshot);

        return response with { Sheet = sheet };
    }

    private static void RequireViewOrExecuteReport(PermissionSnapshot snapshot, string reportCode)
    {
        if (!CanViewOrExecuteReport(snapshot, reportCode))
            throw new NgbPermissionDeniedException(new NgbPermissionKey(NgbResourceKinds.Report, reportCode, NgbPermissionActions.View));
    }

    private static bool CanViewOrExecuteReport(PermissionSnapshot snapshot, string reportCode)
        => Has(snapshot, NgbResourceKinds.Report, reportCode, NgbPermissionActions.View)
           || Has(snapshot, NgbResourceKinds.Report, reportCode, NgbPermissionActions.Execute)
           || HasAdminBackedReportAccess(snapshot, reportCode);

    private static void RequireExecuteReport(PermissionSnapshot snapshot, string reportCode)
    {
        if (Has(snapshot, NgbResourceKinds.Report, reportCode, NgbPermissionActions.Execute)
            || HasAdminBackedReportAccess(snapshot, reportCode))
        {
            return;
        }

        throw new NgbPermissionDeniedException(
            new NgbPermissionKey(NgbResourceKinds.Report, reportCode, NgbPermissionActions.Execute));
    }

    private static bool HasAnyAdminBackedReportAccess(PermissionSnapshot snapshot)
        => Has(snapshot, NgbResourceKinds.Admin, NgbPermissionResources.PostingLog, NgbPermissionActions.View)
           || Has(snapshot, NgbResourceKinds.Admin, NgbPermissionResources.Integrity, NgbPermissionActions.View);

    private static bool HasAdminBackedReportAccess(PermissionSnapshot snapshot, string reportCode)
    {
        if (string.Equals(reportCode, AccountingReportCodes.PostingLog, StringComparison.OrdinalIgnoreCase))
            return Has(snapshot, NgbResourceKinds.Admin, NgbPermissionResources.PostingLog, NgbPermissionActions.View);

        if (string.Equals(reportCode, AccountingReportCodes.Consistency, StringComparison.OrdinalIgnoreCase))
            return Has(snapshot, NgbResourceKinds.Admin, NgbPermissionResources.Integrity, NgbPermissionActions.View);

        return false;
    }

    private static IReadOnlyList<ReportDefinitionDto> FilterDefinitions(
        IReadOnlyList<ReportDefinitionDto> definitions,
        PermissionSnapshot snapshot)
    {
        var result = new List<ReportDefinitionDto>(definitions.Count);
        foreach (var definition in definitions)
        {
            if (CanViewOrExecuteReport(snapshot, definition.ReportCode))
                result.Add(definition);
        }

        return result;
    }

    private static ReportSheetDto ScrubForbiddenCellActions(ReportSheetDto sheet, PermissionSnapshot snapshot)
    {
        if (snapshot.IsBootstrapAdmin)
            return sheet;

        return sheet with
        {
            Rows = ScrubRows(sheet.Rows, snapshot),
            HeaderRows = sheet.HeaderRows is null ? null : ScrubRows(sheet.HeaderRows, snapshot)
        };
    }

    private static IReadOnlyList<ReportSheetRowDto> ScrubRows(
        IReadOnlyList<ReportSheetRowDto> rows,
        PermissionSnapshot snapshot)
    {
        var result = new ReportSheetRowDto[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var cells = new ReportCellDto[row.Cells.Count];

            for (var j = 0; j < row.Cells.Count; j++)
            {
                var cell = row.Cells[j];
                cells[j] = cell.Action is null || IsCellActionAllowed(cell.Action, snapshot)
                    ? cell
                    : cell with { Action = null };
            }

            result[i] = row with { Cells = cells };
        }

        return result;
    }

    private static bool IsCellActionAllowed(ReportCellActionDto action, PermissionSnapshot snapshot)
    {
        return action.Kind switch
        {
            ReportCellActionKinds.OpenDocument when !string.IsNullOrWhiteSpace(action.DocumentType)
                => Has(snapshot, NgbResourceKinds.Document, action.DocumentType, NgbPermissionActions.View),
            ReportCellActionKinds.OpenCatalog when !string.IsNullOrWhiteSpace(action.CatalogType)
                => Has(snapshot, NgbResourceKinds.Catalog, action.CatalogType, NgbPermissionActions.View),
            ReportCellActionKinds.OpenReport when action.Report is not null
                => CanViewOrExecuteReport(snapshot, action.Report.ReportCode),
            ReportCellActionKinds.OpenAccount
                => Has(snapshot, NgbResourceKinds.Admin, NgbPermissionResources.ChartOfAccounts, NgbPermissionActions.View),
            _ => false
        };
    }

    private static bool Has(PermissionSnapshot snapshot, string resourceKind, string resourceCode, string actionCode)
        => snapshot.Has(resourceKind, resourceCode, actionCode);
}
