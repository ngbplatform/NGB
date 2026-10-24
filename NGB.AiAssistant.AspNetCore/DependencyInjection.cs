using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NGB.Core.Features;
using NGB.Mcp;
using NGB.Mcp.AspNetCore;
using NGB.Runtime.DependencyInjection;
using NGB.Tools.Http;

namespace NGB.AiAssistant.AspNetCore;

public static class DependencyInjection
{
    /// <summary>Registers the assistant and the application's chosen IChatClient only when its feature is enabled.</summary>
    public static IServiceCollection AddNgbAiAssistant( this IServiceCollection services,
        IConfiguration configuration,
        Action<IServiceCollection> configureProvider)
    {
        services.AddNgbFeatureManagement(configuration);
        if (!configuration.GetValue<bool>($"FeatureManagement:{NgbFeatures.AiAssistant}"))
            return services;

        configureProvider(services);
        if (services.All(service => service.ServiceType != typeof(IChatClient)))
            throw new InvalidOperationException("AI Assistant requires an IChatClient implementation.");

        services.AddOptions<AiAssistantOptions>()
            .Bind(configuration.GetSection(AiAssistantOptions.SectionName))
            .Validate(options => options.IsValid(), "Invalid AI Assistant limits.")
            .ValidateOnStart();

        services.AddOptions<AiAssistantHttpOptions>()
            .Bind(configuration.GetSection(AiAssistantOptions.SectionName))
            .Validate(options => options.ReportPageSize is > 0 and <= McpReadOptions.MaximumReportPageSize, "Invalid Assistant report page size.")
            .Validate(options => options.McpLoopbackPort is null or > 0 and <= 65535, "Invalid MCP loopback port.")
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddSingleton<AiAssistantAdmission>();
        services.AddScoped<AiAssistantRunner>();
        services.TryAddScoped<IAiAssistantToolSessionFactory, McpToolSessionFactory>();
        services.AddHttpClient(McpToolSessionFactory.ClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(provider => CreateMcpHandler(provider, configuration));

        services.AddSingleton<AssistantRegistration>();

        return services;
    }

    public static IEndpointRouteBuilder MapNgbAiAssistant(this IEndpointRouteBuilder endpoints)
    {
        if (endpoints.ServiceProvider.GetService<AssistantRegistration>() is null)
            return endpoints;

        _ = endpoints.ServiceProvider.GetNgbMcpEndpoint();

        using var validationHandler = CreateMcpHandler(endpoints.ServiceProvider,
            endpoints.ServiceProvider.GetRequiredService<IConfiguration>());

        endpoints.MapPost("/api/ai-assistant/chat", AiAssistantHttpEndpoint.HandleAsync)
            .RequireAuthorization(NGB.Mcp.AspNetCore.DependencyInjection.CurrentUserPolicyName);

        endpoints.MapGet("/api/ai-assistant", (Func<HttpContext, Task<IResult>>)AiAssistantHttpEndpoint.SettingsAsync)
            .RequireAuthorization(NGB.Mcp.AspNetCore.DependencyInjection.CurrentUserPolicyName);

        return endpoints;
    }

    private static SocketsHttpHandler CreateMcpHandler(IServiceProvider services, IConfiguration configuration)
    {
        var settings = services.GetRequiredService<IOptions<AiAssistantHttpOptions>>().Value;
        var endpoint = services.GetNgbMcpEndpoint();
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false };

        if (settings.McpLoopbackPort is { } port)
        {
            if (!HttpEndpointUri.IsLocalhost(endpoint))
                throw new InvalidOperationException("McpLoopbackPort is supported only for a canonical localhost MCP endpoint.");

            handler.ConnectCallback = async (_, ct) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(IPAddress.Loopback, port, ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            };
        }

        if (settings.TrustDevelopmentServerCertificate)
        {
            if (!services.GetRequiredService<IHostEnvironment>().IsDevelopment() || !HttpEndpointUri.IsLocalhost(endpoint))
                throw new InvalidOperationException("TrustDevelopmentServerCertificate requires Development and a canonical localhost MCP endpoint.");

            var path = configuration["Kestrel:Certificates:Default:Path"];
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("Configure the existing Kestrel development certificate before trusting it for MCP.");

            using var certificate = X509CertificateLoader.LoadPkcs12FromFile(path, configuration["Kestrel:Certificates:Default:Password"]);
            var expected = certificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);
            var notBefore = certificate.NotBefore.ToUniversalTime();
            var notAfter = certificate.NotAfter.ToUniversalTime();

            handler.SslOptions.RemoteCertificateValidationCallback = (_, remote, _, errors) =>
                errors == SslPolicyErrors.None
                || errors == SslPolicyErrors.RemoteCertificateChainErrors
                && remote is not null
                && remote.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256) == expected
                && DateTime.UtcNow >= notBefore && DateTime.UtcNow <= notAfter;
        }

        return handler;
    }

    private sealed class AssistantRegistration;
}
