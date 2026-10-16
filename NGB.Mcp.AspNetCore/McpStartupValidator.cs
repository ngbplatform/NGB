using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Authentication;
using NGB.Runtime.Security;

namespace NGB.Mcp.AspNetCore;

internal sealed class McpStartupValidator(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        _ = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(DependencyInjection.BearerScheme);
        _ = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<McpAuthenticationOptions>>()
            .Get(DependencyInjection.Scheme);

        var registry = scope.ServiceProvider.GetRequiredService<McpReadToolRegistry>();
        registry.ValidateHandlers(scope.ServiceProvider);

        var permissions = scope.ServiceProvider.GetRequiredService<PermissionDefinitionRegistry>();
        await registry.ValidatePermissionsAsync(permissions, ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
