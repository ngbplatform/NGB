using Microsoft.AspNetCore.Mvc;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;

namespace NGB.Api.Controllers;

public abstract class ReportControllerBase(
    IReportDefinitionProvider definitions,
    IReportEngine engine,
    IReportVariantService variants,
    IReportExportService exports) : ControllerBase
{
    [HttpGet("~/api/report-definitions")]
    public Task<IReadOnlyList<ReportDefinitionDto>> GetAllDefinitions(CancellationToken ct)
        => definitions.GetAllDefinitionsAsync(ct);

    [HttpGet("~/api/report-definitions/{reportCode}")]
    public Task<ReportDefinitionDto> GetDefinition([FromRoute] string reportCode, CancellationToken ct)
        => definitions.GetDefinitionAsync(reportCode, ct);

    [HttpPost("~/api/reports/{reportCode}/execute")]
    public Task<ReportExecutionResponseDto> Execute(
        [FromRoute] string reportCode,
        [FromBody] ReportExecutionRequestDto request,
        CancellationToken ct)
        => engine.ExecuteAsync(reportCode, request, ct);

    [HttpPost("~/api/reports/{reportCode}/export/xlsx")]
    public async Task<IActionResult> ExportXlsx(
        [FromRoute] string reportCode,
        [FromBody] ReportExportRequestDto request,
        CancellationToken ct)
    {
        var sheet = await engine.ExecuteExportSheetAsync(reportCode, request, ct);
        var bytes = await exports.ExportXlsxAsync(sheet, sheet.Meta?.Title, ct);
        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            BuildExportFileName(reportCode, sheet.Meta?.Title));
    }

    [HttpGet("~/api/reports/{reportCode}/variants")]
    public Task<IReadOnlyList<ReportVariantDto>> GetVariants([FromRoute] string reportCode, CancellationToken ct)
        => variants.GetAllAsync(reportCode, ct);

    [HttpGet("~/api/reports/{reportCode}/variants/{variantCode}")]
    public async Task<ActionResult<ReportVariantDto>> GetVariant(
        [FromRoute] string reportCode,
        [FromRoute] string variantCode,
        CancellationToken ct)
    {
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
        => variants.SaveAsync(variant with { ReportCode = reportCode, VariantCode = variantCode }, ct);

    [HttpDelete("~/api/reports/{reportCode}/variants/{variantCode}")]
    public async Task<IActionResult> DeleteVariant(
        [FromRoute] string reportCode,
        [FromRoute] string variantCode,
        CancellationToken ct)
    {
        await variants.DeleteAsync(reportCode, variantCode, ct);
        return NoContent();
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
