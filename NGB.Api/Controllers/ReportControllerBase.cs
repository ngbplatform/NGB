using Microsoft.AspNetCore.Mvc;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Core.Security;
using NGB.Runtime.Security;

namespace NGB.Api.Controllers;

public abstract class ReportControllerBase(
    IReportDefinitionProvider definitions,
    IReportEngine engine,
    IReportVariantService variants,
    IReportExportService exports,
    INgbAccessChecker access) : ControllerBase
{
    [HttpGet("~/api/report-definitions")]
    public async Task<IReadOnlyList<ReportDefinitionDto>> GetAllDefinitions(CancellationToken ct)
    {
        var all = await definitions.GetAllDefinitionsAsync(ct);
        var result = new List<ReportDefinitionDto>(all.Count);
       
        foreach (var definition in all)
        {
            if (await CanViewOrExecuteReportAsync(definition.ReportCode, ct))
                result.Add(definition);
        }

        return result;
    }

    [HttpGet("~/api/report-definitions/{reportCode}")]
    public async Task<ReportDefinitionDto> GetDefinition([FromRoute] string reportCode, CancellationToken ct)
    {
        await RequireViewOrExecuteReportAsync(reportCode, ct);
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
        await access.RequireAsync(NgbResourceKinds.Report, reportCode, NgbPermissionActions.Export, ct);
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
        await RequireViewOrExecuteReportAsync(reportCode, ct);
        return await variants.GetAllAsync(reportCode, ct);
    }

    [HttpGet("~/api/reports/{reportCode}/variants/{variantCode}")]
    public async Task<ActionResult<ReportVariantDto>> GetVariant(
        [FromRoute] string reportCode,
        [FromRoute] string variantCode,
        CancellationToken ct)
    {
        await RequireViewOrExecuteReportAsync(reportCode, ct);
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
        if (existing?.IsShared == true)
        {
            await access.RequireAsync(NgbResourceKinds.Report, reportCode, NgbPermissionActions.ManageSharedVariants, ct);
        }
        else
        {
            
            await RequireAnyReportPermissionAsync(
                reportCode,
                [NgbPermissionActions.DeleteVariant, NgbPermissionActions.ManageSharedVariants],
                ct);
        }

        await variants.DeleteAsync(reportCode, variantCode, ct);
        return NoContent();
    }

    private async Task<ReportExecutionResponseDto> ExecuteCoreAsync(
        string reportCode,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        await access.RequireAsync(NgbResourceKinds.Report, reportCode, NgbPermissionActions.Execute, ct);
        var response = await engine.ExecuteAsync(reportCode, request, ct);
        var sheet = await ScrubForbiddenCellActionsAsync(response.Sheet, ct);
        return response with { Sheet = sheet };
    }

    private async Task<ReportVariantDto> SaveVariantCoreAsync(
        string reportCode,
        string variantCode,
        ReportVariantDto variant,
        CancellationToken ct)
    {
        await access.RequireAsync(
            NgbResourceKinds.Report,
            reportCode,
            variant.IsShared ? NgbPermissionActions.ManageSharedVariants : NgbPermissionActions.SavePrivateVariant,
            ct);

        return await variants.SaveAsync(variant with { ReportCode = reportCode, VariantCode = variantCode }, ct);
    }

    private async Task RequireViewOrExecuteReportAsync(string reportCode, CancellationToken ct)
    {
        if (!await CanViewOrExecuteReportAsync(reportCode, ct))
            throw new NgbPermissionDeniedException(new NgbPermissionKey(NgbResourceKinds.Report, reportCode, NgbPermissionActions.View));
    }

    private async Task<bool> CanViewOrExecuteReportAsync(string reportCode, CancellationToken ct)
        => await access.HasAsync(NgbResourceKinds.Report, reportCode, NgbPermissionActions.View, ct)
           || await access.HasAsync(NgbResourceKinds.Report, reportCode, NgbPermissionActions.Execute, ct);

    private async Task RequireAnyReportPermissionAsync(
        string reportCode,
        IReadOnlyList<string> actions,
        CancellationToken ct)
    {
        foreach (var action in actions)
        {
            if (await access.HasAsync(NgbResourceKinds.Report, reportCode, action, ct))
                return;
        }

        throw new NgbPermissionDeniedException(new NgbPermissionKey(NgbResourceKinds.Report, reportCode, actions[0]));
    }

    private async Task<ReportSheetDto> ScrubForbiddenCellActionsAsync(ReportSheetDto sheet, CancellationToken ct)
        => sheet with
        {
            Rows = await ScrubRowsAsync(sheet.Rows, ct),
            HeaderRows = sheet.HeaderRows is null ? null : await ScrubRowsAsync(sheet.HeaderRows, ct)
        };

    private async Task<IReadOnlyList<ReportSheetRowDto>> ScrubRowsAsync(
        IReadOnlyList<ReportSheetRowDto> rows,
        CancellationToken ct)
    {
        var result = new ReportSheetRowDto[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var cells = new ReportCellDto[row.Cells.Count];

            for (var j = 0; j < row.Cells.Count; j++)
            {
                var cell = row.Cells[j];
                cells[j] = cell.Action is null || await IsCellActionAllowedAsync(cell.Action, ct)
                    ? cell
                    : cell with { Action = null };
            }

            result[i] = row with { Cells = cells };
        }

        return result;
    }

    private async Task<bool> IsCellActionAllowedAsync(ReportCellActionDto action, CancellationToken ct)
    {
        return action.Kind switch
        {
            ReportCellActionKinds.OpenDocument when !string.IsNullOrWhiteSpace(action.DocumentType)
                => await access.HasAsync(NgbResourceKinds.Document, action.DocumentType, NgbPermissionActions.View, ct),
            ReportCellActionKinds.OpenCatalog when !string.IsNullOrWhiteSpace(action.CatalogType)
                => await access.HasAsync(NgbResourceKinds.Catalog, action.CatalogType, NgbPermissionActions.View, ct),
            ReportCellActionKinds.OpenReport when action.Report is not null
                => await CanViewOrExecuteReportAsync(action.Report.ReportCode, ct),
            ReportCellActionKinds.OpenAccount
                => await access.HasAsync(NgbResourceKinds.Admin, NgbPermissionResources.ChartOfAccounts, NgbPermissionActions.View, ct),
            _ => false
        };
    }

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
