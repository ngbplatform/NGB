using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.Jobs;
using NGB.OperationalRegisters.Contracts;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.BackgroundJobs.IntegrationTests.Jobs;

public sealed class OperationalRegistersFinalizeDirtyMonthsJob_BoundedWork_P0Tests
{
    [Fact]
    public async Task RunAsync_PassesDefaultMaxItems_AndRecordsMetrics()
    {
        // Arrange
        var maintenance = new FakeMaintenanceService(finalized: 7);
        var metrics = new CapturingMetrics();

        var job = new OperationalRegistersFinalizeDirtyMonthsJob(
            maintenance,
            NullLogger<OperationalRegistersFinalizeDirtyMonthsJob>.Instance,
            metrics);

        // Act
        await job.RunAsync(CancellationToken.None);

        // Assert
        maintenance.FinalizeDirtyCalls.Should().ContainSingle();
        maintenance.FinalizeDirtyCalls[0].MaxItems.Should().Be(50);

        metrics.SetCalls.Should().Contain(x => x.Name == "max_items" && x.Value == 50);
        metrics.SetCalls.Should().Contain(x => x.Name == "finalized_count" && x.Value == 0);
        metrics.SetCalls.Should().Contain(x => x.Name == "finalized_count" && x.Value == 7);

        var snapshot = metrics.Snapshot();
        snapshot["max_items"].Should().Be(50);
        snapshot["finalized_count"].Should().Be(7);
    }

    private sealed record FinalizeDirtyCall(int MaxItems);

    private sealed class FakeMaintenanceService : IOperationalRegisterAdminMaintenanceService
    {
        private readonly int _finalized;

        public FakeMaintenanceService(int finalized)
        {
            _finalized = finalized;
        }

        public List<FinalizeDirtyCall> FinalizeDirtyCalls { get; } = new();

        public Task<int> FinalizeDirtyAsync(int maxItems = 50, CancellationToken ct = default)
        {
            FinalizeDirtyCalls.Add(new FinalizeDirtyCall(maxItems));
            return Task.FromResult(_finalized);
        }

        public Task<int> FinalizeRegisterDirtyAsync(Guid registerId, int maxPeriods = 50, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task MarkFinalizationDirtyAsync(Guid registerId, DateOnly periodMonth, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationalRegisterPhysicalSchemaHealth?> EnsurePhysicalSchemaByIdAsync(Guid registerId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationalRegisterPhysicalSchemaHealthReport> EnsurePhysicalSchemaForAllAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingMetrics : IJobRunMetrics
    {
        private readonly Dictionary<string, long> _lastValues = new(StringComparer.Ordinal);

        public List<(string Name, long Value)> SetCalls { get; } = new();

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

            name = name.Trim();
            SetCalls.Add((name, value));
            _lastValues[name] = value;
        }

        public IReadOnlyDictionary<string, long> Snapshot()
        {
            return _lastValues.Count == 0
                ? new Dictionary<string, long>(0)
                : new Dictionary<string, long>(_lastValues);
        }
    }
}
