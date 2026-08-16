using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.BackgroundJobs.Jobs.Internal;
using NGB.BackgroundJobs.Observability;

namespace NGB.BackgroundJobs.Tests.Jobs;

public sealed class EnsureSchemaJobRunnerFullCoverageTests
{
    [Fact]
    public async Task HealthyReport_CompletesAndRecordsAllBoundaryCounters()
    {
        var metrics = new JobRunMetrics();
        var cancellationToken = new CancellationTokenSource().Token;
        CancellationToken received = default;

        await EnsureSchemaJobRunner.RunAsync(
            NullLogger.Instance,
            metrics,
            "schema.ensure",
            "Registers",
            new FixedTimeProvider(),
            token =>
            {
                received = token;
                return Task.FromResult((TotalCount: 0, OkCount: 0));
            },
            cancellationToken);

        received.Should().Be(cancellationToken);
        metrics.Snapshot().Should().Contain(new Dictionary<string, long>
        {
            ["registers_total"] = 0,
            ["registers_ok"] = 0,
            ["registers_failed"] = 0,
            ["has_failures"] = 0
        });
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }
}
