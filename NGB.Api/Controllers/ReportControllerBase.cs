using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;
using NGB.Api.Reporting;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Core.Security;
using NGB.Hosting.AspNetCore.Identity;
using NGB.Runtime.Reporting;
using NGB.Runtime.Security;
using NGB.Tools.Exceptions;

namespace NGB.Api.Controllers;

public abstract class ReportControllerBase(
    IReportDefinitionProvider definitions,
    IReportEngine engine,
    IReportVariantService variants,
    IReportDownloadService downloads,
    INgbAccessChecker access,
    NgbSecurityCache cache)
    : ControllerBase
{
    [HttpGet("~/api/report-definitions")]
    public async Task<IReadOnlyList<ReportDefinitionDto>> GetAllDefinitions(CancellationToken ct)
    {
        var reads = new PermissionAwareReportReadService(definitions, engine, access, cache);
        return await reads.GetAllDefinitionsAsync(ct);
    }

    [HttpGet("~/api/report-definitions/{reportCode}")]
    public async Task<ReportDefinitionDto> GetDefinition([FromRoute] string reportCode, CancellationToken ct)
    {
        var reads = new PermissionAwareReportReadService(definitions, engine, access, cache);
        return await reads.GetDefinitionAsync(reportCode, ct);
    }

    [HttpPost("~/api/reports/{reportCode}/execute")]
    [ReportRequestBudget]
    public Task<ReportExecutionResponseDto> Execute(
        [FromRoute] string reportCode,
        [FromBody] ReportExecutionRequestDto request,
        CancellationToken ct)
        => new PermissionAwareReportReadService(definitions, engine, access, cache).ExecuteAsync(reportCode, request, ct);

    [HttpPost("~/api/reports/{reportCode}/export/xlsx")]
    [ReportRequestBudget(download: true)]
    public async Task<IActionResult> ExportXlsx(
        [FromRoute] string reportCode,
        [FromBody] ReportExportRequestDto request,
        CancellationToken ct)
    {
        var snapshot = await access.GetSnapshotAsync(ct);
        Require(snapshot, NgbResourceKinds.Report, reportCode, NgbPermissionActions.Export);

        var download = await downloads.PrepareAsync(reportCode, request, ct);

        // Also release the read session if a later MVC filter replaces or rejects this result.
        HttpContext?.Response.RegisterForDisposeAsync(download);

        return new ReportDownloadResult(download, BuildExportFileName(reportCode, download.Title));
    }

    // Native navigation lets the browser download the response without a JavaScript Blob.
    [HttpPost("~/api/reports/{reportCode}/export/xlsx/form")]
    [Consumes("application/x-www-form-urlencoded")]
    [FormBearerToken]
    [RequestSizeLimit(FormBearerTokenAttribute.MaximumBodyBytes)]
    [ReportRequestBudget(download: true)]
    public Task<IActionResult> ExportXlsxForm(
        [FromRoute] string reportCode,
        [FromForm] string request,
        [FromServices] IOptions<JsonOptions> json,
        CancellationToken ct)
    {
        ReportExportRequestDto? export;
        try
        {
            export = JsonSerializer.Deserialize<ReportExportRequestDto>(request, json.Value.JsonSerializerOptions);
        }
        catch (JsonException)
        {
            throw new NgbArgumentInvalidException(nameof(request), "Invalid report export request.");
        }

        if (export is null)
            throw new NgbArgumentInvalidException(nameof(request), "A report export request is required.");

        return ExportXlsx(reportCode, export, ct);
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

        return variant ?? throw new ReportVariantNotFoundException(reportCode, variantCode);
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
            variant.IsShared
                ? NgbPermissionActions.ManageSharedVariants
                : NgbPermissionActions.SavePrivateVariant);

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
