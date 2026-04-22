using FluentAssertions;
using NGB.BackgroundJobs.Observability;

namespace NGB.BackgroundJobs.Tests.Observability;

public sealed class JobRunMetrics_P0Tests
{
    [Fact]
    public void Increment_And_Set_Work_And_Snapshot_IsImmutableCopy()
    {
        var m = new JobRunMetrics();

        m.Increment("a");
        m.Increment("a", 2);
        m.Set("b", 10);
        m.Set("b", 11);

        var s1 = m.Snapshot();
        s1["a"].Should().Be(3);
        s1["b"].Should().Be(11);

        // mutate live metrics after snapshot
        m.Increment("a", 5);
        m.Set("b", 99);

        var s2 = m.Snapshot();
        s2["a"].Should().Be(8);
        s2["b"].Should().Be(99);

        // old snapshot must not change
        s1["a"].Should().Be(3);
        s1["b"].Should().Be(11);
    }

    [Fact]
    public void Empty_Names_And_Zero_Deltas_Are_Ignored()
    {
        var m = new JobRunMetrics();

        m.Increment("   ");
        m.Increment("", 123);
        m.Increment("x", 0);
        m.Set("   ", 1);

        m.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public void Names_Are_Trimmed()
    {
        var m = new JobRunMetrics();

        m.Increment(" a ");
        m.Increment("a");
        m.Set(" b ", 7);

        var s = m.Snapshot();
        s.Should().ContainKey("a");
        s["a"].Should().Be(2);
        s.Should().ContainKey("b");
        s["b"].Should().Be(7);
    }
}
