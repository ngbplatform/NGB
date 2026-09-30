using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Reports;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReporting_AdmissionFaults_P0Tests(PmIntegrationFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("page", 2, 4)]
    [InlineData("json", 2, 4)]
    [InlineData("form", 2, 4)]
    [InlineData("page", 12, 48)]
    [InlineData("page", 12, 0)]
    public async Task Http_burst_preserves_fifo_and_report_concurrency_with_or_without_queue(string kind, int concurrent, int queued)
    {
        await using var h = new ReportAdmissionHarness(fixture, concurrent, queued);
        var requests = new List<Task<HttpResponseMessage>>();
        for (var index = 0; index < concurrent; index++)
        {
            requests.Add(h.Send(kind, index.ToString()));
            (await h.Work.NextAsync()).Should().Be(index.ToString());
        }
        for (var index = 0; index < queued; index++)
        {
            requests.Add(h.Send(kind, (concurrent + index).ToString()));
            await h.WaitForQueueAsync(kind, index + 1);
        }
        h.Work.Started.Should().Be(concurrent, "waiting requests must not begin report preparation");
        using var excess = await h.Send(kind, "excess");
        await AssertBusyAsync(excess);
        for (var index = 0; index < queued; index++)
        {
            h.Work.Release(index.ToString());
            (await h.Work.NextAsync()).Should().Be((concurrent + index).ToString());
            await h.WaitForQueueAsync(kind, queued - index - 1);
            h.Work.Active.Should().Be(concurrent);
        }
        h.Work.ReleaseAll();
        foreach (var request in requests) using (var response = await request.WaitAsync(TimeSpan.FromSeconds(10)))
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        h.Work.Peak.Should().Be(concurrent);
        h.Work.Started.Should().Be(concurrent + queued);
        h.Work.Disposed.Should().Be(concurrent + queued);
        await AssertEmptyAsync(h, kind, concurrent);
    }

    [Theory]
    [InlineData("page")]
    [InlineData("json")]
    [InlineData("form")]
    public async Task Http_queue_deadline_rejects_before_report_work_and_accepts_replacement(string kind)
    {
        await using var h = new ReportAdmissionHarness(fixture, queue: 1, queueSeconds: 1);
        var active = h.Send(kind, "active");
        (await h.Work.NextAsync()).Should().Be("active");
        using var expired = await h.Send(kind, "expired").WaitAsync(TimeSpan.FromSeconds(5));
        await AssertBusyAsync(expired);
        await h.WaitForQueueAsync(kind, 0);
        h.Work.Started.Should().Be(1);
        var replacement = h.Send(kind, "replacement");
        await h.WaitForQueueAsync(kind, 1);
        h.Work.Release("active");
        (await h.Work.NextAsync()).Should().Be("replacement");
        h.Work.ReleaseAll();
        using var first = await active;
        using var second = await replacement;
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        h.Work.Started.Should().Be(2);
        await AssertEmptyAsync(h, kind);
    }

    [Theory]
    [InlineData("page", 0)] [InlineData("page", 1)] [InlineData("page", 2)]
    [InlineData("json", 0)] [InlineData("json", 1)] [InlineData("json", 2)]
    [InlineData("form", 0)] [InlineData("form", 1)] [InlineData("form", 2)]
    public async Task Http_cancelled_waiter_at_head_middle_or_tail_never_executes_and_preserves_order(string kind, int cancelledIndex)
    {
        await using var h = new ReportAdmissionHarness(fixture, queue: 3);
        using var cancellation = new CancellationTokenSource();
        var active = h.Send(kind, "active");
        await h.Work.NextAsync();
        var pending = new List<Task<HttpResponseMessage>>();
        for (var i = 0; i < 3; i++)
        {
            pending.Add(h.Send(kind, i.ToString(), i == cancelledIndex ? cancellation.Token : default));
            await h.WaitForQueueAsync(kind, i + 1);
        }
        cancellation.Cancel();
        await ((Func<Task>)(async () => { using var response = await pending[cancelledIndex]; })).Should().ThrowAsync<OperationCanceledException>();
        await h.WaitForQueueAsync(kind, 2);
        pending.Add(h.Send(kind, "replacement"));
        await h.WaitForQueueAsync(kind, 3);
        h.Work.Started.Should().Be(1);
        h.Work.Release("active");
        foreach (var id in Enumerable.Range(0, 3).Where(i => i != cancelledIndex).Select(i => i.ToString()).Append("replacement"))
        {
            (await h.Work.NextAsync()).Should().Be(id);
            h.Work.Release(id);
        }
        using var initial = await active;
        foreach (var request in pending.Where((_, index) => index != cancelledIndex))
            using (var response = await request) response.StatusCode.Should().Be(HttpStatusCode.OK);
        h.Work.Started.Should().Be(4);
        h.Work.Ticket(cancelledIndex.ToString()).Completed.Task.IsCompleted.Should().BeFalse();
        await AssertEmptyAsync(h, kind);
    }

    [Theory]
    [InlineData("page")]
    [InlineData("json")]
    [InlineData("form")]
    public async Task Http_active_disconnect_cancels_work_and_gives_slot_to_waiter(string kind)
    {
        await using var h = new ReportAdmissionHarness(fixture);
        using var cancellation = new CancellationTokenSource();
        var active = h.Send(kind, "active", cancellation.Token);
        await h.Work.NextAsync();
        var pending = h.Send(kind, "next");
        await h.WaitForQueueAsync(kind, 1);
        cancellation.Cancel();
        await ((Func<Task>)(async () => { using var response = await active; })).Should().ThrowAsync<OperationCanceledException>();
        (await h.Work.NextAsync()).Should().Be("next");
        await h.Work.Ticket("active").Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        h.Work.Release("next");
        using var completed = await pending;
        completed.StatusCode.Should().Be(HttpStatusCode.OK);
        h.Work.Disposed.Should().Be(2);
        await AssertEmptyAsync(h, kind);
    }

    [Theory]
    [InlineData("page", ReportFault.BeforeHeaders)]
    [InlineData("json", ReportFault.Preparation)]
    [InlineData("form", ReportFault.Preparation)]
    [InlineData("json", ReportFault.BeforeHeaders)]
    [InlineData("form", ReportFault.BeforeHeaders)]
    public async Task Http_report_failure_releases_resources_and_admits_next_request(string kind, ReportFault fault)
    {
        await using var h = new ReportAdmissionHarness(fixture);
        h.Work.Ticket("fault").Fault = fault;
        h.Work.Release("fault");
        using var failed = await h.Send(kind, "fault");
        failed.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        h.Work.Release("next");
        using var recovered = await h.Send(kind, "next");
        recovered.StatusCode.Should().Be(HttpStatusCode.OK);
        h.Work.Disposed.Should().Be(2);
        await AssertEmptyAsync(h, kind);
    }

    [Theory]
    [InlineData("page")]
    [InlineData("json")]
    [InlineData("form")]
    public async Task Http_execution_deadline_before_headers_returns_504_and_disposes_work(string kind)
    {
        await using var h = new ReportAdmissionHarness(fixture, executionSeconds: 1);
        using var timedOut = await h.Send(kind, "timeout").WaitAsync(TimeSpan.FromSeconds(5));
        timedOut.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
        (await timedOut.Content.ReadFromJsonAsync<ProblemDetails>())!.Status.Should().Be(504);
        h.Work.Disposed.Should().Be(1);
        h.Work.Release("next");
        using var next = await h.Send(kind, "next");
        next.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertEmptyAsync(h, kind);
    }

    [Theory]
    [InlineData("json", false)] [InlineData("form", false)]
    [InlineData("json", true)] [InlineData("form", true)]
    public async Task Http_partial_download_is_aborted_on_fault_or_deadline_without_appending_json(string kind, bool timeout)
    {
        await using var h = new ReportAdmissionHarness(fixture, executionSeconds: timeout ? 1 : 30);
        var ticket = h.Work.Ticket("partial");
        ticket.Fault = timeout ? ReportFault.PauseAfterHeaders : ReportFault.AfterHeaders;
        using var response = await h.Send(kind, "partial", headersOnly: true).WaitAsync(TimeSpan.FromSeconds(5));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await ticket.HeadersWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using var stream = await response.Content.ReadAsStreamAsync();
        var prefix = new byte[7];
        await stream.ReadExactlyAsync(prefix).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Encoding.UTF8.GetString(prefix).Should().Be("partial");
        if (!timeout) h.Work.Release("partial");
        var error = await ((Func<Task>)(async () => { var buffer = new byte[1024]; await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }))
            .Should().ThrowAsync<Exception>();
        error.Which.Should().NotBeOfType<TimeoutException>("the server must abort, not leave a hung partial response");
        await ticket.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        h.Work.Release("next");
        using var next = await h.Send(kind, "next");
        next.StatusCode.Should().Be(HttpStatusCode.OK);
        h.Work.Disposed.Should().Be(2);
        await AssertEmptyAsync(h, kind);
    }

    [Theory]
    [InlineData("page")]
    [InlineData("json")]
    [InlineData("form")]
    public async Task Http_execution_deadline_starts_after_admission_not_while_waiting(string kind)
    {
        await using var h = new ReportAdmissionHarness(fixture, queueSeconds: 10, executionSeconds: 1);
        using var held = await h.Budget.AcquireAsync(ReportAdmissionHarness.Download(kind), default);
        var waiting = h.Send(kind, "waiting");
        await h.WaitForQueueAsync(kind, 1);
        await Task.Delay(TimeSpan.FromMilliseconds(1200));
        waiting.IsCompleted.Should().BeFalse();
        h.Work.Started.Should().Be(0);
        h.Work.Release("waiting");
        held!.Dispose();
        using var response = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertEmptyAsync(h, kind);
    }

    [Theory]
    [InlineData("page")]
    [InlineData("json")]
    [InlineData("form")]
    public async Task Http_invalid_request_releases_admission_without_starting_report_work(string kind)
    {
        await using var h = new ReportAdmissionHarness(fixture);
        using HttpContent invalid = kind == "form"
            ? new FormUrlEncodedContent(new Dictionary<string, string> { ["request"] = "{" })
            : new StringContent("{", Encoding.UTF8, "application/json");
        using var response = await h.Client.PostAsync(ReportAdmissionHarness.Route(kind), invalid);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        h.Work.Started.Should().Be(0);
        h.Work.Release("valid");
        using var valid = await h.Send(kind, "valid");
        valid.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertEmptyAsync(h, kind);
    }

    [Theory]
    [InlineData("page", "json")]
    [InlineData("json", "page")]
    [InlineData("form", "page")]
    public async Task Saturated_http_request_kind_does_not_consume_other_kind_capacity(string blocked, string independent)
    {
        await using var h = new ReportAdmissionHarness(fixture, queue: 0);
        var active = h.Send(blocked, "held");
        await h.Work.NextAsync();
        using var rejected = await h.Send(blocked, "rejected");
        await AssertBusyAsync(rejected);
        h.Work.Release("independent");
        using var other = await h.Send(independent, "independent");
        other.StatusCode.Should().Be(HttpStatusCode.OK);
        active.IsCompleted.Should().BeFalse();
        h.Work.Release("held");
        using var finished = await active;
        finished.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertEmptyAsync(h, blocked);
        await AssertEmptyAsync(h, independent);
    }

    private static async Task AssertBusyAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(3));
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Status.Should().Be(429);
    }

    private static async Task AssertEmptyAsync(ReportAdmissionHarness h, string kind, int permits = 1)
    {
        await h.WaitForQueueAsync(kind, 0);
        h.Budget.Statistics(ReportAdmissionHarness.Download(kind)).CurrentAvailablePermits.Should().Be(permits);
        h.Work.Active.Should().Be(0);
    }
}
