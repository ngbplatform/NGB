using Microsoft.Extensions.Configuration;
using NGB.Api;

namespace NGB.Watchdog.HealthChecks;

public class WebClientHealthCheck(IHttpClientFactory httpClientFactory, IConfiguration config)
    : BaseHttpExternalHealthCheck(httpClientFactory, config["WebClient"]!, "WebClient", "HealthCheckHttpClient");
