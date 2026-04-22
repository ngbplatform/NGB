using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NGB.Api;

public abstract class BaseHttpExternalHealthCheck(
    IHttpClientFactory httpClientFactory,
    string url,
    string name,
    string? httpClientName = null)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var httpClient = string.IsNullOrWhiteSpace(httpClientName)
                ? httpClientFactory.CreateClient()
                : httpClientFactory.CreateClient(httpClientName);

            var response = await httpClient.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode)
                return HealthCheckResult.Healthy($"{name} is reachable and responding.");

            return HealthCheckResult.Unhealthy($"{name} URL returned status code {response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Error checking {name} URL: {ex.Message}");
        }
    }
}
