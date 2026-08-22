using Microsoft.AspNetCore.Mvc;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Core.Security;
using NGB.Runtime.Security;

namespace NGB.Api.Controllers;

public abstract class ReportControllerBase(
    IReportDefinitionProvider definitions,
    IReportEngine engine,
    IReportVariantService variants,
    IReportExportService exports,
    INgbAccessChecker access,
    NgbSecurityCache cache) : ControllerBase
{
    [HttpGet("~/api/report-definitions")]
    public async Task<IReadOnlyList<ReportDefinitionDto>> GetAllDefinitions(CancellationToken ct)
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

    [HttpGet("~/api/report-definitions/{reportCode}")]
    public async Task<ReportDefinitionDto> GetDefinition([FromRoute] string reportCode, CancellationToken ct)
    {
        var snapshot = await access.GetSnapshotAsync(ct);
        RequireViewOrExecuteReport(snapshot, reportCode);
        return await definitions.GetDefinitionAsync(reportCode, ct);
    }

    [HttpPost("~/api/reports/{reportCode}/execute")]
    public Task<ReportExecutionResponseDto> Execute(
        [FromRoute] string reportCode,
        [FromBody] ReportExecutionRequestDto request,
        CancellationToken ct)
        => ExecuteCoreAsync(reportCode, request, ct);

    [HttpPost("~/api/reports/{reportCode}/export/xlsx")]
    public async Task<IActionResult> ExportXlsx(
        [FromRoute] string reportCode,
        [FromBody] ReportExportRequestDto request,
        CancellationToken ct)
    {
        var snapshot = await access.GetSnapshotAsync(ct);
        Require(snapshot, NgbResourceKinds.Report, reportCode, NgbPermissionActions.Export);
        var sheet = await engine.ExecuteExportSheetAsync(reportCode, request, ct);
        var bytes = await exports.ExportXlsxAsync(sheet, sheet.Meta?.Title, ct);

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            BuildExportFileName(reportCode, sheet.Meta?.Title));
    }

    [HttpGet("~/api/reports/{reportCode}/variants")]
    public async Task<IReadOnlyList<ReportVariantDto>> GetVariants([FromRoute] string reportCode, CancellationToken ct)
    {
        var snapshot = await access.GetSnapshotAsync(ct);
        RequireViewOrExecuteReport(snapshot, reportCode);
        return await variants.GetAllAsync(reportCode, ct);
    }

    [HttpGet("~/api/reports/{reportCode}/variants/{variantCode}")]
    public async Task<ActionResult<ReportVariantDto>> GetVariant(
        [FromRoute] string reportCode,
        [FromRoute] string variantCode,
        CancellationToken ct)
    {
        var snapshot = await access.GetSnapshotAsync(ct);
        RequireViewOrExecuteReport(snapshot, reportCode);
        var variant = await variants.GetAsync(reportCode, variantCode, ct);

        return variant is null
            ? throw new ReportVariantNotFoundException(reportCode, variantCode)
            : variant;
    }

    [HttpPut("~/api/reports/{reportCode}/variants/{variantCode}")]
    public Task<ReportVariantDto> SaveVariant(
        [FromRoute] string reportCode,
        [FromRoute] string variantCode,
        [FromBody] ReportVariantDto variant,
        CancellationToken ct)
        => SaveVariantCoreAsync(reportCode, variantCode, variant, ct);

    [HttpDelete("~/api/reports/{reportCode}/variants/{variantCode}")]
    public async Task<IActionResult> DeleteVariant(
        [FromRoute] string reportCode,
        [FromRoute] string variantCode,
        CancellationToken ct)
    {
        var existing = await variants.GetAsync(reportCode, variantCode, ct);
        var snapshot = await access.GetSnapshotAsync(ct);
        if (existing?.IsShared == true)
        {
            Require(snapshot, NgbResourceKinds.Report, reportCode, NgbPermissionActions.ManageSharedVariants);
        }
        else
        {
            RequireAnyReportPermission(
                snapshot,
                reportCode,
                [NgbPermissionActions.DeleteVariant, NgbPermissionActions.ManageSharedVariants]);
        }

        await variants.DeleteAsync(reportCode, variantCode, ct);
        return NoContent();
    }

    private async Task<ReportExecutionResponseDto> ExecuteCoreAsync(
        string reportCode,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var snapshot = await access.GetSnapshotAsync(ct);
        RequireExecuteReport(snapshot, reportCode);
        var response = await engine.ExecuteAsync(reportCode, request, ct);
        var sheet = ScrubForbiddenCellActions(response.Sheet, snapshot);
        return response with { Sheet = sheet };
    }

    private async Task<ReportVariantDto> SaveVariantCoreAsync(
        string reportCode,
        string variantCode,
        ReportVariantDto variant,
        CancellationToken ct)
    {
        var snapshot = await access.GetSnapshotAsync(ct);
        Require(
            snapshot,
            NgbResourceKinds.Report,
            reportCode,
            variant.IsShared ? NgbPermissionActions.ManageSharedVariants : NgbPermissionActions.SavePrivateVariant);

        return await variants.SaveAsync(variant with { ReportCode = reportCode, VariantCode = variantCode }, ct);
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

    private static void RequireAnyReportPermission(
        PermissionSnapshot snapshot,
        string reportCode,
        IReadOnlyList<string> actions)
    {
        foreach (var action in actions)
        {
            if (Has(snapshot, NgbResourceKinds.Report, reportCode, action))
                return;
        }

        throw new NgbPermissionDeniedException(new NgbPermissionKey(NgbResourceKinds.Report, reportCode, actions[0]));
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

    private static void Require(PermissionSnapshot snapshot, string resourceKind, string resourceCode, string actionCode)
    {
        if (snapshot.Has(resourceKind, resourceCode, actionCode))
            return;

        throw new NgbPermissionDeniedException(new NgbPermissionKey(resourceKind, resourceCode, actionCode));
    }

    private static bool Has(PermissionSnapshot snapshot, string resourceKind, string resourceCode, string actionCode)
        => snapshot.Has(resourceKind, resourceCode, actionCode);

    private static string BuildExportFileName(string reportCode, string? title)
    {
        var baseName = string.IsNullOrWhiteSpace(title) ? reportCode : title;
        var safe = new string(baseName
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray())
            .Trim('-');

        if (string.IsNullOrWhiteSpace(safe))
            safe = "report";

        return $"{safe}.xlsx";
    }
}
