using Microsoft.Extensions.DependencyInjection;

namespace NGB.Watchdog.HealthChecks;

public static class DependencyInjection
{
    public static IHealthChecksBuilder AddWebClient(this IHealthChecksBuilder builder,
        string name = "Web Client (Vue.js)")
    {
        return builder.AddCheck<WebClientHealthCheck>(name);
    }
}