using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Api.Reporting;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;

namespace NGB.PropertyManagement.Api.IntegrationTests.Reports;

/// <summary>Real authenticated HTTP/MVC pipeline; only report work is controlled by explicit gates.</summary>
internal sealed class ReportAdmissionHarness : IAsyncDisposable
{
    private readonly PmApiFactory _root;
    private readonly WebApplicationFactory<Program> _app;
    private readonly List<Task<HttpResponseMessage>> _requests = [];

    public ControlledReportWork Work { get; } = new();
    public HttpClient Client { get; }
    public ReportRequestBudget Budget => _app.Services.GetRequiredService<ReportRequestBudget>();

    public ReportAdmissionHarness(PmIntegrationFixture fixture, int concurrent = 1, int queue = 4, int queueSeconds = 10, int executionSeconds = 30)
    {
        _root = new PmApiFactory(fixture, new Dictionary<string, string?>
        {
            ["Reporting:Requests:ConcurrentPages"] = concurrent.ToString(),
            ["Reporting:Requests:ConcurrentDownloads"] = concurrent.ToString(),
            ["Reporting:Requests:QueuedPages"] = queue.ToString(),
            ["Reporting:Requests:QueuedDownloads"] = queue.ToString(),
            ["Reporting:Requests:PageQueueTimeoutSeconds"] = queueSeconds.ToString(),
            ["Reporting:Requests:DownloadQueueTimeoutSeconds"] = queueSeconds.ToString(),
            ["Reporting:Requests:PageTimeoutSeconds"] = executionSeconds.ToString(),
            ["Reporting:Requests:DownloadTimeoutSeconds"] = executionSeconds.ToString()
        });
        _app = _root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IReportEngine>(); services.AddSingleton<IReportEngine>(Work);
            services.RemoveAll<IReportDownloadService>(); services.AddSingleton<IReportDownloadService>(Work);
        }));
        using var identity = _root.CreateClient();
        Client = _app.CreateClient();
        Client.DefaultRequestHeaders.Authorization = identity.DefaultRequestHeaders.Authorization;
    }

    public static bool Download(string kind) => kind != "page";
    public static string Route(string kind) => "/api/reports/accounting.ledger.analysis/" +
        (kind == "page" ? "execute" : kind == "form" ? "export/xlsx/form" : "export/xlsx");

    public Task<HttpResponseMessage> Send(string kind, string id, CancellationToken ct = default, bool headersOnly = false)
    {
        Work.Ticket(id);
        var parameters = new Dictionary<string, string> { ["testId"] = id };
        HttpContent content = kind switch
        {
            "page" => JsonContent.Create(new ReportExecutionRequestDto(Parameters: parameters)),
            "form" => new FormUrlEncodedContent(new Dictionary<string, string>
                { ["request"] = JsonSerializer.Serialize(new ReportExportRequestDto(Parameters: parameters)) }),
            _ => JsonContent.Create(new ReportExportRequestDto(Parameters: parameters))
        };
        var request = new HttpRequestMessage(HttpMethod.Post, Route(kind)) { Content = content };
        var task = SendOwnedAsync(request, ct, headersOnly);
        _requests.Add(task);
        return task;
    }

    private async Task<HttpResponseMessage> SendOwnedAsync(HttpRequestMessage request, CancellationToken ct, bool headersOnly)
    {
        using (request) return await Client.SendAsync(request,
            headersOnly ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, ct);
    }

    public async Task WaitForQueueAsync(string kind, int count)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (Budget.Statistics(Download(kind)).CurrentQueuedCount != count) await Task.Delay(1, deadline.Token);
    }

    public async ValueTask DisposeAsync()
    {
        Work.ReleaseAll();
        try
        {
            foreach (var pending in _requests)
            {
                try
                {
                    (await pending.WaitAsync(TimeSpan.FromSeconds(10))).Dispose();
                }
                catch (Exception error) when
                    (error is OperationCanceledException or HttpRequestException or IOException)
                {
                }
            }
        }
        finally
        {
            Client.Dispose();
            await _app.DisposeAsync();
            await _root.DisposeAsync();
        }
    }
}

public enum ReportFault { None, Preparation, BeforeHeaders, AfterHeaders, PauseAfterHeaders }

internal sealed class ControlledReportWork : IReportEngine, IReportDownloadService
{
    private readonly ConcurrentDictionary<string, WorkTicket> _tickets = new();
    private int _active, _peak, _started, _disposed;
    public int Active => Volatile.Read(ref _active);
    public int Peak => Volatile.Read(ref _peak);
    public int Started => Volatile.Read(ref _started);
    public int Disposed => Volatile.Read(ref _disposed);
    public Channel<string> Starts { get; } = Channel.CreateUnbounded<string>();
    public WorkTicket Ticket(string id) => _tickets.GetOrAdd(id, _ => new());
    public void Release(string id) => Ticket(id).Release.TrySetResult();
    public void ReleaseAll() { foreach (var ticket in _tickets.Values) ticket.Release.TrySetResult(); }
    public async Task<string> NextAsync() => await Starts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

    private WorkTicket Begin(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ticket = Ticket(id);
        var active = Interlocked.Increment(ref _active);
        int peak;
        do { peak = Volatile.Read(ref _peak); if (peak >= active) break; }
        while (Interlocked.CompareExchange(ref _peak, active, peak) != peak);
        Interlocked.Increment(ref _started);
        Starts.Writer.TryWrite(id);
        return ticket;
    }

    private void End(WorkTicket ticket)
    {
        Interlocked.Decrement(ref _active);
        Interlocked.Increment(ref _disposed);
        ticket.Completed.TrySetResult();
    }

    public async Task<ReportExecutionResponseDto> ExecuteAsync(string code, ReportExecutionRequestDto request, CancellationToken ct)
    {
        var ticket = Begin(request.Parameters!["testId"], ct);
        try
        {
            await ticket.Release.Task.WaitAsync(ct);
            if (ticket.Fault != ReportFault.None) throw new IOException("Injected report execution fault.");
            return new(new([], []), 0, 1, null, false, null);
        }
        finally { End(ticket); }
    }

    public Task<ReportSheetDto> ExecuteExportSheetAsync(string code, ReportExportRequestDto request, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<IReportDownload> PrepareAsync(string code, ReportExportRequestDto request, CancellationToken ct)
    {
        var ticket = Begin(request.Parameters!["testId"], ct);
        if (ticket.Fault == ReportFault.Preparation)
        {
            End(ticket);
            throw new IOException("Injected report preparation fault.");
        }
        return Task.FromResult<IReportDownload>(new Download(this, ticket));
    }

    private sealed class Download(ControlledReportWork owner, WorkTicket ticket) : IReportDownload
    {
        private int _disposed;
        public string Title => "Admission test";
        public async Task WriteAsync(Stream destination, CancellationToken ct)
        {
            if (ticket.Fault is ReportFault.AfterHeaders or ReportFault.PauseAfterHeaders)
            {
                await destination.WriteAsync("partial"u8.ToArray(), ct);
                await destination.FlushAsync(ct);
                ticket.HeadersWritten.TrySetResult();
            }
            await ticket.Release.Task.WaitAsync(ct);
            if (ticket.Fault is ReportFault.BeforeHeaders or ReportFault.AfterHeaders) throw new IOException("Injected export write fault.");
            await destination.WriteAsync("complete"u8.ToArray(), ct);
        }
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.End(ticket);
            return ValueTask.CompletedTask;
        }
    }

    public sealed class WorkTicket
    {
        public ReportFault Fault { get; set; }
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource HeadersWritten { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
