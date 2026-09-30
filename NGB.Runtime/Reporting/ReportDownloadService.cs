using System.Runtime.CompilerServices;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.Reporting;
using NGB.Runtime.Reporting.Streaming;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting;

public sealed class ReportDownloadService(
    IEnumerable<IStreamingReportExecutor> executors,
    IReportDefinitionProvider definitions,
    IReportLayoutValidator validator,
    ReportVariantRequestResolver variants,
    ReportFilterScopeExpander filters,
    IReportReadSession session,
    IStreamingReportExportService exporter)
    : IReportDownloadService
{
    public async Task<IReportDownload> PrepareAsync(
        string reportCode,
        ReportExportRequestDto request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var definition = await definitions.GetDefinitionAsync(reportCode, ct);
        var execution = await variants.ResolveAsync(
            reportCode,
            new(request.Layout, request.Filters, request.Parameters, request.VariantCode),
            ct);

        validator.Validate(definition, execution);
        execution = await filters.ExpandAsync(new(definition), ReportEngine.NormalizeRequestMaps(execution), ct);

        var executor = executors.SingleOrDefault(x => string.Equals(x.ReportCode, reportCode, StringComparison.OrdinalIgnoreCase))
            ?? executors.Single(x => x.ReportCode == "*");

        var prepared = executor.Prepare(definition, execution with
        {
            VariantCode = null,
            Cursor = null,
            Offset = 0
        });

        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            await session.BeginAsync(lifetime.Token);
        }
        catch
        {
            lifetime.Dispose(); throw;
        }
        
        var rows = executor.ReadAsync(prepared, lifetime.Token).GetAsyncEnumerator(lifetime.Token);
        
        try
        {
            // Establish the schema and surface validation/query errors before HTTP headers are sent.
            var hasFirst = await rows.MoveNextAsync();
            return new Download(executor.Template(prepared), rows, hasFirst, session, exporter, lifetime);
        }
        catch
        {
            try
            {
                await rows.DisposeAsync();
            }
            finally
            {
                try
                {
                    await session.EndAsync(CancellationToken.None);
                }
                finally
                {
                    lifetime.Dispose();
                }
            }
            throw;
        }
    }

    private sealed class Download(
        ReportSheetDto template,
        IAsyncEnumerator<ReportRowWrite> rows,
        bool hasFirst,
        IReportReadSession session,
        IStreamingReportExportService exporter, CancellationTokenSource lifetime)
        : IReportDownload
    {
        private bool _written;
        private bool _disposed;

        public string? Title => template.Meta?.Title;

        public async Task WriteAsync(Stream destination, CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_written)
                throw new InvalidOperationException("An export can only be written once.");

            _written = true;

            await using var registration = ct.Register(lifetime.Cancel);
            await exporter.WriteXlsxAsync(destination, template, ReadRows(lifetime.Token), lifetime.Token);
        }

        private async IAsyncEnumerable<ReportSheetRowDto> ReadRows([EnumeratorCancellation] CancellationToken ct)
        {
            var hasRow = hasFirst;
            var ordinal = 0;

            while (hasRow)
            {
                ct.ThrowIfCancellationRequested();

                if (rows.Current.Ordinal != ordinal++)
                    throw new NgbInvariantViolationException("A streaming export must emit each row exactly once in order.");

                yield return rows.Current.Row;

                hasRow = await rows.MoveNextAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                await rows.DisposeAsync();
            }
            finally
            {
                try
                {
                    await session.EndAsync(CancellationToken.None);
                }
                finally
                {
                    lifetime.Dispose();
                }
            }
        }
    }
}
