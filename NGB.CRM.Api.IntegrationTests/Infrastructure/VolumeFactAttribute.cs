using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Infrastructure;

[AttributeUsage(AttributeTargets.Method)]
public sealed class VolumeFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "NGB_RUN_VOLUME_TESTS";

    public VolumeFactAttribute()
    {
        var value = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.Equals(value, bool.TrueString, StringComparison.OrdinalIgnoreCase) && value != "1")
        {
            Skip = $"Set {EnvironmentVariable}=true to run CRM production-volume integration tests.";
        }
    }
}
