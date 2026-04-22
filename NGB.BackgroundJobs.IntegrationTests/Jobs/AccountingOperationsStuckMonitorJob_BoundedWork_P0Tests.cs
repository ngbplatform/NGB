using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.Jobs;
using NGB.Persistence.Readers.PostingState;
using Xunit;

namespace NGB.BackgroundJobs.IntegrationTests.Jobs;

public sealed class AccountingOperationsStuckMonitorJob_BoundedWork_P0Tests
{
    [Fact]
    public async Task RunAsync_WhenNoStaleRows_RecordsProblem0_AndDoesNotThrow()
    {
        // Arrange
        var reader = new CapturingPostingLogReader(new PostingStatePage(
            Records: Array.Empty<PostingStateRecord>(),
            HasMore: false,
            NextCursor: null));

        var metrics = new CapturingMetrics();

        var job = new AccountingOperationsStuckMonitorJob(
            reader,
            NullLogger<AccountingOperationsStuckMonitorJob>.Instance,
            metrics);

        // Act
        await job.RunAsync(CancellationToken.None);

        // Assert: bounded read request
        reader.Requests.Should().ContainSingle();
        var request = reader.Requests[0];

        request.PageSize.Should().Be(25);
        request.Status.Should().Be(PostingStateStatus.StaleInProgress);
        request.StaleAfter.Should().Be(TimeSpan.FromMinutes(10));

        // The window is deterministic: (startedAt - 30 days) .. (startedAt + 1 day) => 31 days.
        (request.ToUtc - request.FromUtc).Should().BeCloseTo(TimeSpan.FromDays(31), TimeSpan.FromSeconds(10));

        // Assert: metrics
        var snapshot = metrics.Snapshot();
        snapshot["page_size"].Should().Be(25);
        snapshot["stale_count"].Should().Be(0);
        snapshot["warnings_logged"].Should().Be(0);
        snapshot["problem"].Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_WhenStaleRowsExist_RecordsProblem1_AndDoesNotThrow()
    {
        // Arrange
        var now = DateTime.UtcNow;

        var records = new[]
        {
            new PostingStateRecord(
                DocumentId: Guid.CreateVersion7(),
                Operation: PostingOperation.Post,
                StartedAtUtc: now.AddMinutes(-25),
                CompletedAtUtc: null,
                Status: PostingStateStatus.StaleInProgress,
                Duration: null,
                Age: TimeSpan.FromMinutes(25)),
            new PostingStateRecord(
                DocumentId: Guid.CreateVersion7(),
                Operation: PostingOperation.Repost,
                StartedAtUtc: now.AddMinutes(-12),
                CompletedAtUtc: null,
                Status: PostingStateStatus.StaleInProgress,
                Duration: null,
                Age: TimeSpan.FromMinutes(12)),
        };

        var reader = new CapturingPostingLogReader(new PostingStatePage(
            Records: records,
            HasMore: false,
            NextCursor: null));

        var metrics = new CapturingMetrics();

        var job = new AccountingOperationsStuckMonitorJob(
            reader,
            NullLogger<AccountingOperationsStuckMonitorJob>.Instance,
            metrics);

        // Act (must not throw; it only logs warnings)
        await job.RunAsync(CancellationToken.None);

        // Assert: bounded request + metrics reflect found rows
        reader.Requests.Should().ContainSingle();
        reader.Requests[0].PageSize.Should().Be(25);

        var snapshot = metrics.Snapshot();
        snapshot["page_size"].Should().Be(25);
        snapshot["stale_count"].Should().Be(records.Length);
        snapshot["warnings_logged"].Should().Be(records.Length);
        snapshot["problem"].Should().Be(1);
    }

    private sealed class CapturingPostingLogReader : IPostingStateReader
    {
        private readonly PostingStatePage _page;

        public CapturingPostingLogReader(PostingStatePage page)
        {
            _page = page;
        }

        public List<PostingStatePageRequest> Requests { get; } = new();

        public Task<PostingStatePage> GetPageAsync(PostingStatePageRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(_page);
        }
    }

    private sealed class CapturingMetrics : IJobRunMetrics
    {
        private readonly Dictionary<string, long> _lastValues = new(StringComparer.Ordinal);

        public void Increment(string name, long delta = 1)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;
            if (delta == 0)
                return;

            name = name.Trim();

            _lastValues.TryGetValue(name, out var existing);
            _lastValues[name] = existing + delta;
        }

        public void Set(string name, long value)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            _lastValues[name.Trim()] = value;
        }

        public IReadOnlyDictionary<string, long> Snapshot()
        {
            return _lastValues.Count == 0
                ? new Dictionary<string, long>(0)
                : new Dictionary<string, long>(_lastValues);
        }
    }
}
