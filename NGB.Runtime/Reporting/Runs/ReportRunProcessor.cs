using System.Text.Json;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NGB.Application.Abstractions.Services;
using NGB.Persistence.Reporting;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting.Runs;

/// <summary>Each attempt reads one source snapshot and publishes only after every result row is stored.
/// A lost lease fences the previous worker; partial attempts are never exposed.</summary>
public sealed class ReportRunProcessor(IServiceScopeFactory scopes, ILogger<ReportRunProcessor> logger)
{
    public async Task CleanupAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IReportRunStore>().CleanupAsync(ct);
    }

    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        await using var writeScope = scopes.CreateAsyncScope();
        await using var readScope = scopes.CreateAsyncScope();

        var store = writeScope.ServiceProvider.GetRequiredService<IReportRunStore>();
        var executors = readScope.ServiceProvider.GetServices<IStreamingReportExecutor>().ToArray();
        var codes = executors
            .Where(e => e.ReportCode != "*")
            .Select(e => e.ReportCode);

        if (executors.Any(e => e.ReportCode == "*"))
        {
            codes = codes
                .Concat((await readScope.ServiceProvider.GetRequiredService<IReportDefinitionProvider>()
                        .GetAllDefinitionsAsync(ct))
                    .Select(d => d.ReportCode));
        }

        var run = await store.ClaimAsync(codes.Select(c => c.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray(), ct);
        
        if (run is null)
            return false;

        using var work = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var heartbeats = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var heartbeat = RenewAsync(run, work, heartbeats.Token);

        try
        {
            var executor = executors.SingleOrDefault(e
                               => string.Equals(e.ReportCode, run.ReportCode, StringComparison.OrdinalIgnoreCase))
                           ?? executors.Single(e => e.ReportCode == "*");

            await readScope.ServiceProvider.GetRequiredService<IReportReadSession>().BeginAsync(work.Token);

            var batch = new Dictionary<int, string>(256);
            var count = 0;

            await foreach (var row in executor.ReadAsync(run.PreparedJson, work.Token))
            {
                work.Token.ThrowIfCancellationRequested();

                if (row.Ordinal < 0)
                    throw new NgbInvariantViolationException("Report row ordinal must not be negative.");

                batch[row.Ordinal] = JsonSerializer.Serialize(row.Row);
                count = Math.Max(count, checked(row.Ordinal + 1));

                if (batch.Count < 256)
                    continue;

                await store.WriteAsync(run.Id, run.Attempt, batch.Keys.ToArray(), batch.Values.ToArray(), work.Token);
                batch.Clear();
            }

            if (batch.Count > 0)
                await store.WriteAsync(run.Id, run.Attempt, batch.Keys.ToArray(), batch.Values.ToArray(), work.Token);

            if (await store.CompleteAsync(run.Id, run.Attempt, JsonSerializer.Serialize(executor.Template(run.PreparedJson)), count, work.Token))
                logger.LogInformation("Report {ReportCode} run {RunId} completed with {RowCount} rows", run.ReportCode, run.Id, count);
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
             // Cancelled or abandoned: lease recovery handles shutdown.
        }
        catch (Exception ex) when (ex is DbException { IsTransient: true } or TimeoutException)
        {
            // Stop renewing the lease. Another attempt can retry after its backoff, up to the queue's attempt limit.
            logger.LogWarning(ex, "Report {ReportCode} run {RunId} will retry after a transient failure", run.ReportCode, run.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Report {ReportCode} run {RunId} failed", run.ReportCode, run.Id);
            await store.FailAsync(run.Id, run.Attempt, ReportRunFailure.Capture(ex), ct);
        }
        finally
        {
            await heartbeats.CancelAsync();
            await heartbeat;
        }

        return true;
    }

    private async Task RenewAsync(StoredReportRun run, CancellationTokenSource work, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(ct))
            {
                await using var scope = scopes.CreateAsyncScope();

                if (await scope.ServiceProvider.GetRequiredService<IReportRunStore>().RenewAsync(run.Id, run.Attempt, ct))
                    continue;

                await work.CancelAsync();

                return;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Report run {RunId} lost its lease", run.Id);
            await work.CancelAsync();
        }
    }
}
