using System.Diagnostics.Metrics;
using FluentAssertions;
using NGB.Runtime.Observability;
using Xunit;

namespace NGB.Runtime.Tests.Observability;

[Collection(TelemetrySerialCollection.Name)]
public sealed class NgbFeatureTelemetryTests
{
    [Fact]
    public void Operational_health_gauges_clamp_negative_values_and_publish_current_values()
    {
        var measurements = new Dictionary<string, double>(StringComparer.Ordinal);
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == NgbFeatureTelemetry.MeterName)
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, value, _, _) => measurements[instrument.Name] = value);
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, _, _) => measurements[instrument.Name] = value);
        listener.Start();

        NgbFeatureTelemetry.ObserveOperationalHealth(-1, -2.5, -3, -4);
        listener.RecordObservableInstruments();

        measurements["ngb.outbox.pending"].Should().Be(0);
        measurements["ngb.outbox.oldest_age"].Should().Be(0);
        measurements["ngb.work_center.tasks_open"].Should().Be(0);
        measurements["ngb.work_center.tasks_overdue"].Should().Be(0);

        NgbFeatureTelemetry.ObserveOperationalHealth(11, 12.5, 13, 14);
        listener.RecordObservableInstruments();

        measurements["ngb.outbox.pending"].Should().Be(11);
        measurements["ngb.outbox.oldest_age"].Should().Be(12.5);
        measurements["ngb.work_center.tasks_open"].Should().Be(13);
        measurements["ngb.work_center.tasks_overdue"].Should().Be(14);
    }
}
