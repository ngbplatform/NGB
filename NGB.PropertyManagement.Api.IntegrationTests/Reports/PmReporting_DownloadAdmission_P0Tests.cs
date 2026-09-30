using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Api.Reporting;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Reports;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReporting_DownloadAdmission_P0Tests(PmIntegrationFixture fixture) : IAsyncLifetime
{
    private const string Route = "/api/reports/accounting.ledger.analysis/export/xlsx";

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Json_and_native_form_share_bounded_queue_until_streaming_response_is_disposed()
    {
        var downloads = new ControlledDownloads();
        await using var root = new PmApiFactory(fixture);
        await using var app = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IReportDownloadService>();
            services.AddSingleton<IReportDownloadService>(downloads);
        }));
        using var identity = root.CreateClient();
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = identity.DefaultRequestHeaders.Authorization;
        var budget = app.Services.GetRequiredService<ReportRequestBudget>();
        var requests = new List<Task<HttpResponseMessage>>();
        try
        {
            requests.Add(Send(client, form: false));
            requests.Add(Send(client, form: true));
            await downloads.Started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            await downloads.Started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            budget.Statistics(true).CurrentAvailablePermits.Should().Be(0);

            for (var index = 0; index < 4; index++) requests.Add(Send(client, form: index % 2 == 0));
            await WaitForQueue(budget, 4);
            downloads.Prepared.Should().Be(2, "queued requests must not prepare a report or open its read session");
            downloads.Disposed.Should().Be(0, "the streaming responses still own both execution slots");

            using var excess = await Send(client, form: false).WaitAsync(TimeSpan.FromSeconds(5));
            excess.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            excess.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(3));
            var problem = await excess.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
            problem!.Status.Should().Be(429);
            downloads.Prepared.Should().Be(2);
        }
        finally
        {
            downloads.Release.TrySetResult();
            var responses = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(10));
            foreach (var response in responses)
            {
                using (response)
                {
                    response.StatusCode.Should().Be(HttpStatusCode.OK);
                    response.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
                    (await response.Content.ReadAsStringAsync()).Should().Be("controlled-download-body");
                }
            }
        }
        downloads.Prepared.Should().Be(6);
        downloads.Disposed.Should().Be(6);
        budget.Statistics(true).CurrentAvailablePermits.Should().Be(2);
        budget.Statistics(true).CurrentQueuedCount.Should().Be(0);
    }

    [Fact]
    public async Task Disconnected_queued_http_request_never_prepares_a_report_and_frees_its_place()
    {
        var downloads = new ControlledDownloads();
        await using var root = new PmApiFactory(fixture, new Dictionary<string, string?>
        {
            ["Reporting:Requests:ConcurrentDownloads"] = "1",
            ["Reporting:Requests:QueuedDownloads"] = "1"
        });
        await using var app = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IReportDownloadService>();
            services.AddSingleton<IReportDownloadService>(downloads);
        }));
        using var identity = root.CreateClient();
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = identity.DefaultRequestHeaders.Authorization;
        var budget = app.Services.GetRequiredService<ReportRequestBudget>();
        var active = Send(client, form: false);
        try
        {
            await downloads.Started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            using var disconnected = new CancellationTokenSource();
            var pending = Send(client, form: true, disconnected.Token);
            await WaitForQueue(budget, 1);
            disconnected.Cancel();
            var observe = async () => { using var response = await pending; };
            await observe.Should().ThrowAsync<OperationCanceledException>();
            await WaitForQueue(budget, 0);
            downloads.Prepared.Should().Be(1);
            budget.Statistics(true).CurrentAvailablePermits.Should().Be(0);
        }
        finally
        {
            downloads.Release.TrySetResult();
            using var response = await active.WaitAsync(TimeSpan.FromSeconds(10));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        using var recovered = await Send(client, form: true).WaitAsync(TimeSpan.FromSeconds(10));
        recovered.StatusCode.Should().Be(HttpStatusCode.OK);
        downloads.Prepared.Should().Be(2);
        downloads.Disposed.Should().Be(2);
        budget.Statistics(true).CurrentAvailablePermits.Should().Be(1);
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, bool form, CancellationToken ct = default)
        => form
            ? client.PostAsync(Route + "/form", new FormUrlEncodedContent(new Dictionary<string, string> { ["request"] = "{}" }), ct)
            : client.PostAsJsonAsync(Route, new ReportExportRequestDto(), ct);

    private static async Task WaitForQueue(ReportRequestBudget budget, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (budget.Statistics(true).CurrentQueuedCount != count)
            await Task.Delay(10, timeout.Token);
    }

    // Controlled output isolates HTTP admission and streaming ownership. The neighboring
    // XLSX export tests exercise real report data and validate the OpenXML workbook.
    private sealed class ControlledDownloads : IReportDownloadService
    {
        private int _prepared;
        private int _disposed;
        public int Prepared => Volatile.Read(ref _prepared);
        public int Disposed => Volatile.Read(ref _disposed);
        public Channel<bool> Started { get; } = Channel.CreateUnbounded<bool>();
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReportDownload> PrepareAsync(string reportCode, ReportExportRequestDto request, CancellationToken ct)
        {
            Interlocked.Increment(ref _prepared);
            return Task.FromResult<IReportDownload>(new Download(this));
        }

        private sealed class Download(ControlledDownloads owner) : IReportDownload
        {
            private int _disposed;
            public string Title => "Controlled download";
            public async Task WriteAsync(Stream destination, CancellationToken ct)
            {
                owner.Started.Writer.TryWrite(true);
                await owner.Release.Task.WaitAsync(ct);
                await destination.WriteAsync("controlled-download-body"u8.ToArray(), ct);
            }
            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Increment(ref owner._disposed);
                return ValueTask.CompletedTask;
            }
        }
    }
}
