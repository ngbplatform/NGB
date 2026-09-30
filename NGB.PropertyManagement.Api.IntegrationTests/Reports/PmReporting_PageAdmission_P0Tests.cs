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
public sealed class PmReporting_PageAdmission_P0Tests(PmIntegrationFixture fixture) : IAsyncLifetime
{
    private const string Route = "/api/reports/accounting.ledger.analysis/execute";
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Http_pages_queue_before_execution_reject_overflow_and_remove_disconnected_waiters()
    {
        var engine = new ControlledEngine();
        await using var root = new PmApiFactory(fixture, new Dictionary<string, string?>
        { ["Reporting:Requests:ConcurrentPages"] = "1", ["Reporting:Requests:QueuedPages"] = "1" });
        await using var app = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<IReportEngine>(); services.AddSingleton<IReportEngine>(engine); }));
        using var identity = root.CreateClient();
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = identity.DefaultRequestHeaders.Authorization;
        var budget = app.Services.GetRequiredService<ReportRequestBudget>();
        var active = client.PostAsJsonAsync(Route, new ReportExecutionRequestDto());
        try
        {
            await engine.Started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            using var disconnect = new CancellationTokenSource();
            var queued = client.PostAsJsonAsync(Route, new ReportExecutionRequestDto(), disconnect.Token);
            await WaitForQueue(budget, 1);
            engine.Executions.Should().Be(1);
            using var excess = await client.PostAsJsonAsync(Route, new ReportExecutionRequestDto());
            excess.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            excess.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(3));
            engine.Executions.Should().Be(1);
            disconnect.Cancel();
            await ((Func<Task>)(async () => { using var response = await queued; })).Should().ThrowAsync<OperationCanceledException>();
            await WaitForQueue(budget, 0);
            var successor = client.PostAsJsonAsync(Route, new ReportExecutionRequestDto());
            await WaitForQueue(budget, 1);
            engine.Executions.Should().Be(1);
            engine.Release.TrySetResult();
            using var resumed = await successor.WaitAsync(TimeSpan.FromSeconds(5));
            resumed.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            engine.Release.TrySetResult();
            using var response = await active.WaitAsync(TimeSpan.FromSeconds(5));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        engine.Executions.Should().Be(2);
        budget.Statistics(false).CurrentAvailablePermits.Should().Be(1);
        budget.Statistics(false).CurrentQueuedCount.Should().Be(0);
        budget.Statistics(true).CurrentAvailablePermits.Should().Be(2);
    }

    [Fact]
    public async Task Http_execution_deadline_returns_504_and_releases_page_capacity()
    {
        var engine = new ControlledEngine();
        await using var root = new PmApiFactory(fixture, new Dictionary<string, string?>
        { ["Reporting:Requests:ConcurrentPages"] = "1", ["Reporting:Requests:PageTimeoutSeconds"] = "1" });
        await using var app = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<IReportEngine>(); services.AddSingleton<IReportEngine>(engine); }));
        using var identity = root.CreateClient();
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = identity.DefaultRequestHeaders.Authorization;
        using var response = await client.PostAsJsonAsync(Route, new ReportExecutionRequestDto()).WaitAsync(TimeSpan.FromSeconds(10));
        response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        problem!.Status.Should().Be(504);
        app.Services.GetRequiredService<ReportRequestBudget>().Statistics(false).CurrentAvailablePermits.Should().Be(1);
        engine.Release.TrySetResult();
        using var recovered = await client.PostAsJsonAsync(Route, new ReportExecutionRequestDto());
        recovered.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task WaitForQueue(ReportRequestBudget budget, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (budget.Statistics(false).CurrentQueuedCount != count) await Task.Delay(1, timeout.Token);
    }

    private sealed class ControlledEngine : IReportEngine
    {
        private int _executions;
        public int Executions => Volatile.Read(ref _executions);
        public Channel<bool> Started { get; } = Channel.CreateUnbounded<bool>();
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ReportExecutionResponseDto> ExecuteAsync(string reportCode, ReportExecutionRequestDto request, CancellationToken ct)
        {
            Interlocked.Increment(ref _executions);
            Started.Writer.TryWrite(true);
            await Release.Task.WaitAsync(ct);
            return new ReportExecutionResponseDto(new([], []), 0, 1, null, false, null);
        }
        public Task<ReportSheetDto> ExecuteExportSheetAsync(string reportCode, ReportExportRequestDto request, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
