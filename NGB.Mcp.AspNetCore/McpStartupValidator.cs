using Microsoft.AspNetCore.Authentication;
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
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<NgbMcpOptions>>().Value;
        var scheme = await scope.ServiceProvider.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetSchemeAsync(settings.AuthenticationScheme);

        if (scheme?.HandlerType != typeof(JwtBearerHandler))
            throw new InvalidOperationException("MCP requires an existing API JWT bearer authentication scheme.");

        var bearer = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(settings.AuthenticationScheme);

        var validation = bearer.TokenValidationParameters;
        if (!validation.ValidateIssuer
            || !validation.ValidateLifetime
            || !validation.ValidateIssuerSigningKey
            || !validation.ValidateAudience
            || !validation.RequireAudience
            || !validation.RequireSignedTokens
            || !validation.RequireExpirationTime
            || bearer.ForwardAuthenticate is not null
            || bearer.ForwardDefault is not null
            || bearer.ForwardDefaultSelector is not null)
        {
            throw new InvalidOperationException("MCP requires API bearer validation of signed, expiring tokens, issuer and audience without authentication forwarding.");
        }

        _ = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<McpAuthenticationOptions>>()
            .Get(DependencyInjection.Scheme);

        var registry = scope.ServiceProvider.GetRequiredService<McpReadToolRegistry>();
        registry.ValidateHandlers(scope.ServiceProvider);

        var permissions = scope.ServiceProvider.GetRequiredService<PermissionDefinitionRegistry>();
        await registry.ValidatePermissionsAsync(permissions, ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
