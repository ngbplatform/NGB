using Xunit;

namespace NGB.Runtime.Tests.Observability;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TelemetrySerialCollection
{
    public const string Name = "Feature telemetry serial";
}
