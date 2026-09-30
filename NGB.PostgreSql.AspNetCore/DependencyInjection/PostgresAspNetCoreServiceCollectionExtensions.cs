using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NGB.Hosting.AspNetCore.ErrorHandling;
using NGB.PostgreSql.AspNetCore.ErrorHandling;
using NGB.PostgreSql.AspNetCore.Health;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.AspNetCore.DependencyInjection;

public static class PostgresAspNetCoreServiceCollectionExtensions
{
    public static IServiceCollection AddNgbPostgresExceptionMapping(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<INgbExceptionHttpMapper, PostgresExceptionHttpMapper>());

        return services;
    }

    public static IHealthChecksBuilder AddNgbPostgresHealthCheck(this IHealthChecksBuilder builder,
        string connectionString,
        string name = "PostgreSQL Server")
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new NgbArgumentRequiredException(nameof(connectionString));

        if (string.IsNullOrWhiteSpace(name))
            throw new NgbArgumentRequiredException(nameof(name));

        return builder.Add(new HealthCheckRegistration(
            name.Trim(),
            _ => new PostgresHealthCheck(connectionString.Trim()),
            HealthStatus.Unhealthy,
            tags: null));
    }
}
