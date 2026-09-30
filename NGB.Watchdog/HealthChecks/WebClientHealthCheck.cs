using Microsoft.Extensions.Configuration;
using NGB.Hosting.AspNetCore.Health;

namespace NGB.Watchdog.HealthChecks;

public class WebClientHealthCheck(IHttpClientFactory httpClientFactory, IConfiguration config)
    : BaseHttpExternalHealthCheck(httpClientFactory, config["WebClient"]!, "WebClient", "HealthCheckHttpClient");
