using System.Runtime.CompilerServices;
using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Persistence.Reporting;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting.Runs;

public sealed class ReportRunService(
    IReportRunStore store,
    IEnumerable<IStreamingReportExecutor> executors,
    IReportDefinitionProvider definitions,
    IReportLayoutValidator validator,
    ReportVariantRequestResolver variants,
    ReportFilterScopeExpander filters,
    IStreamingReportExportService exporter)
    : IReportRunService
{
    public bool Supports(string reportCode)
        => executors.Any(x => x.ReportCode == "*" || string.Equals(x.ReportCode, reportCode, StringComparison.OrdinalIgnoreCase));

    public async Task<ReportRunDto> StartAsync(
        string reportCode,
        string owner,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new NgbArgumentRequiredException(nameof(owner));

        var (executor, prepared) = await PrepareAsync(reportCode, request, ct);
        var id = Guid.CreateVersion7();
        await store.CreateAsync(id, owner, reportCode.ToLowerInvariant(), prepared, ct);

        return (await GetAsync(reportCode, owner, id, ct))!;
    }

    private async Task<(IStreamingReportExecutor Executor, string Prepared)> PrepareAsync(
        string reportCode,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var executor = executors.SingleOrDefault(x => string.Equals(x.ReportCode, reportCode, StringComparison.OrdinalIgnoreCase))
            ?? executors.SingleOrDefault(x => x.ReportCode == "*")
            ?? throw new NgbArgumentInvalidException(nameof(reportCode), "This report does not support saved execution.");

        var definition = await definitions.GetDefinitionAsync(reportCode, ct);
        request = await variants.ResolveAsync(reportCode, request, ct);
        validator.Validate(definition, request);
        request = await filters.ExpandAsync(new ReportDefinitionRuntimeModel(definition), ReportEngine.NormalizeRequestMaps(request), ct);

        var prepared = executor.Prepare(definition, request with
        {
            VariantCode = null,
            Cursor = null,
            Offset = 0,
            Limit = 200,
            DisablePaging = false
        });

        return (executor, prepared);
    }

    public async Task<ReportExecutionResponseDto> ContinueAsync(
        string reportCode,
        string owner,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var parts = request.Cursor?.Split(':');
        if (parts is not { Length: 2 } || !Guid.TryParse(parts[0], out var id) || !int.TryParse(parts[1], out var offset) || offset < 0)
            throw new NgbArgumentInvalidException("cursor", "Invalid report result cursor.");

        var run = await RequireReadyAsync(reportCode, owner, id, ct);
        var (_, prepared) = await PrepareAsync(reportCode, request, ct);

        using var original = JsonDocument.Parse(run.PreparedJson);
        using var current = JsonDocument.Parse(prepared);

        if (!JsonElement.DeepEquals(original.RootElement, current.RootElement))
            throw new NgbArgumentInvalidException("cursor", "Report parameters changed. Run the report again.");

        return await ReadAsync(reportCode, owner, id, offset, request.Limit, ct);
    }

    public async Task<ReportRunDto?> GetAsync(string reportCode, string owner, Guid id, CancellationToken ct)
    {
        var run = await store.FindAsync(id, owner, reportCode.ToLowerInvariant(), ct);
        return run is null ? null : new(run.Id, run.Status, run.RowCount, run.ExpiresAtUtc);
    }

    public Task CancelAsync(string reportCode, string owner, Guid id, CancellationToken ct)
        => store.CancelAsync(id, owner, reportCode.ToLowerInvariant(), ct);

    public async Task<ReportRunDto> WaitAsync(string reportCode, string owner, Guid id, CancellationToken ct)
    {
        while (true)
        {
            var run = await GetAsync(reportCode, owner, id, ct) ?? throw new ReportRunNotFoundException();

            if (run.Status == "Ready")
                return run;

            if (run.Status is "Failed" or "Cancelled")
                await RequireReadyAsync(reportCode, owner, id, ct);

            await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
        }
    }

    public async Task<ReportExecutionResponseDto> ReadAsync(
        string reportCode,
        string owner,
        Guid id,
        int offset,
        int limit,
        CancellationToken ct)
    {
        if (offset < 0)
            throw new NgbArgumentInvalidException(nameof(offset), "Offset must not be negative.");

        var run = await RequireReadyAsync(reportCode, owner, id, ct);
        limit = Math.Clamp(limit, 1, 500);
        var rows = await store.ReadRowsAsync(id, run.Attempt, offset, limit, ct);
        var template = JsonSerializer.Deserialize<ReportSheetDto>(run.TemplateJson!)!;

        if (rows.Count != Math.Min(limit, Math.Max(0, run.RowCount - offset)))
            throw new ReportRunNotFoundException();

        var diagnostics = new Dictionary<string, string>(template.Meta?.Diagnostics ?? new Dictionary<string, string>())
        {
            ["engine"] = "runtime",
            ["runId"] = id.ToString("D"),
            ["paging"] = "saved-result"
        };

        var next = checked(offset + rows.Count);
        
        return new(
            template with
                {
                    Rows = rows.Select(r => JsonSerializer.Deserialize<ReportSheetRowDto>(r)!).ToArray()
                },
            offset,
            limit,
            run.RowCount,
            next < run.RowCount,
            next < run.RowCount ? $"{id:D}:{next}" : null,
            diagnostics);
    }

    public async Task<Stream> ExportAsync(string reportCode, string owner, Guid id, CancellationToken ct)
    {
        var run = await RequireReadyAsync(reportCode, owner, id, ct);
        var template = JsonSerializer.Deserialize<ReportSheetDto>(run.TemplateJson!)!;

        // DeleteOnClose removes the temporary artifact on success, cancellation and HTTP disconnect.
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = 65536,
            Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose
        };

        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        var file = new FileStream(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), options);

        try
        {
            await exporter.WriteXlsxAsync(file, template, RowsAsync(run, ct), ct);
            file.Position = 0;
            return file;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    private async IAsyncEnumerable<ReportSheetRowDto> RowsAsync(
        StoredReportRun run,
        [EnumeratorCancellation] CancellationToken ct)
    {
        for (var offset = 0; offset < run.RowCount;)
        {
            var rows = await store.ReadRowsAsync(run.Id, run.Attempt, offset, 500, ct);
            if (rows.Count == 0)
                throw new ReportRunNotFoundException();

            foreach (var row in rows)
            {
                yield return JsonSerializer.Deserialize<ReportSheetRowDto>(row)!;
            }

            offset += rows.Count;
        }
    }

    private async Task<StoredReportRun> RequireReadyAsync(
        string reportCode,
        string owner,
        Guid id,
        CancellationToken ct)
    {
        var run = await store.FindAsync(id, owner, reportCode.ToLowerInvariant(), ct)
            ?? throw new ReportRunNotFoundException();

        if (run is { Status: "Failed", TemplateJson: not null })
            throw ReportRunFailure.Restore(run.TemplateJson);

        if (run.Status != "Ready")
            throw new ReportRunNotReadyException(run.Status);

        return run;
    }
}
