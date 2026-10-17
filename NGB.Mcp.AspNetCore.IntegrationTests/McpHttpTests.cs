using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NGB.Api.CurrentUser;
using NGB.Application.Abstractions.Features;
using NGB.Core.Features;
using NGB.Core.Security;
using NGB.Hosting.AspNetCore.Identity;
using NGB.Runtime.CurrentActor;
using NGB.Runtime.Security;
using NGB.Testing.Mcp;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Mcp.AspNetCore.IntegrationTests;

public sealed partial class McpHttpTests
{
    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task NativeSdkDiscoversAndInvokesStatelessTools(string protocolVersion)
    {
        await using var host = await TestHost.StartAsync();
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(host.Resource),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + host.Token() }
        }), new McpClientOptions { ProtocolVersion = protocolVersion });
        var tools = await client.ListToolsAsync();
        Assert.Contains(tools, tool => tool.Name == "ngb_get_capabilities");
        var compatibility = tools.Where(tool => tool.ProtocolTool.Meta?[NgbMcpToolMetadata.CompatibilityOnly]?.GetValue<bool>() == true);
        Assert.Equal(["fetch", "search"], compatibility.Select(tool => tool.Name).OrderBy(name => name));
        foreach (var name in new[] { "ngb_search_platform_knowledge", "ngb_get_platform_knowledge" })
        {
            Assert.DoesNotContain(tools, tool => tool.Name == name);
            var removed = await Assert.ThrowsAsync<McpProtocolException>(() => client.CallToolAsync(name,
                new Dictionary<string, object?> { ["id"] = "security-and-permissions", ["query"] = "permissions" }).AsTask());
            Assert.Equal(McpErrorCode.InvalidParams, removed.ErrorCode);
            Assert.EndsWith("The requested read tool is unavailable.", removed.Message);
        }
        var result = await client.CallToolAsync("ngb_list_metadata");
        Assert.NotEqual(true, result.IsError);
        Assert.Empty(result.StructuredContent!.Value.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Empty(result.StructuredContent.Value.GetProperty("sources").EnumerateArray());
        var error = await client.CallToolAsync("fetch", new Dictionary<string, object?> { ["id"] = "https://internal.invalid/secrets" });
        Assert.True(error.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(error.Content)).Text;
        Assert.Contains("invalid_input", text);
        Assert.DoesNotContain("internal.invalid", text);
    }

    [Theory]
    [InlineData("missing", 401)]
    [InlineData("expired", 401)]
    [InlineData("audience", 401)]
    [InlineData("issuer", 401)]
    [InlineData("signature", 401)]
    [InlineData("future", 401)]
    [InlineData("subject", 403)]
    [InlineData("service-account", 403)]
    [InlineData("service", 403)]
    [InlineData("grant", 403)]
    [InlineData("auth-time", 403)]
    [InlineData("id-token", 403)]
    [InlineData("refresh-token", 403)]
    [InlineData("missing-type", 403)]
    [InlineData("unknown-client", 401)]
    [InlineData("inactive", 403)]
    [InlineData("origin", 403)]
    [InlineData("duplicate", 400)]
    public async Task TransportRejectsUntrustedRequests(string scenario, int expectedStatus)
    {
        await using var host = await TestHost.StartAsync();
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, host.Resource);
        if (scenario != "missing")
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", host.Token(scenario));
        if (scenario == "origin")
            request.Headers.Add("Origin", "https://untrusted.invalid");
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Content = new StringContent(scenario == "duplicate"
            ? "{\"jsonrpc\":\"2.0\",\"id\":1,\"id\":2,\"method\":\"tools/list\"}"
            : "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}", Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        Assert.Equal(expectedStatus, (int)response.StatusCode);
        if (expectedStatus == 401)
            Assert.Contains("resource_metadata", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task OAuthMetadataIsPublicAndUsesConfiguredResource()
    {
        await using var host = await TestHost.StartAsync();
        using var client = new HttpClient();
        var metadata = await client.GetStringAsync(host.Origin + "/.well-known/oauth-protected-resource/mcp");
        using var json = JsonDocument.Parse(metadata);
        Assert.Equal(host.Resource, json.RootElement.GetProperty("resource").GetString());
        Assert.Single(json.RootElement.GetProperty("authorization_servers").EnumerateArray());
        Assert.DoesNotContain("ngb_get", metadata);
    }

    [Fact]
    public async Task CustomMappedRouteDerivesItsAddressFromThePublicApiUrl()
    {
        await using var host = await TestHost.StartAsync(pattern: "/integrations/mcp");
        using var http = new HttpClient();
        using var metadata = JsonDocument.Parse(await http.GetStringAsync(host.Origin + "/.well-known/oauth-protected-resource/integrations/mcp"));
        Assert.Equal(host.Resource, metadata.RootElement.GetProperty("resource").GetString());

        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(host.Resource),
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + host.Token() }
        }));
        Assert.NotEmpty(await client.ListToolsAsync());
    }

    [Theory]
    [InlineData("http://pm-keycloak.localhost:7012/realms/ngb-pm", true)]
    [InlineData("http://pm-keycloak.localhost.evil.example:7012/realms/ngb-pm", false)]
    [InlineData("http://keycloak.example:7012/realms/ngb-pm", false)]
    public async Task LocalKeycloakSubdomainWorksWithoutPermittingRemoteHttp(string issuer, bool allowed)
    {
        if (!allowed)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => TestHost.StartAsync(configure: services =>
                services.AddSingleton(new KeycloakSettings { Issuer = issuer })));
            return;
        }

        await using var host = await TestHost.StartAsync(configure: services =>
            services.AddSingleton(new KeycloakSettings { Issuer = issuer }));
        using var http = new HttpClient();
        using var metadata = JsonDocument.Parse(await http.GetStringAsync(host.Origin + "/.well-known/oauth-protected-resource/mcp"));
        Assert.Equal(issuer, Assert.Single(metadata.RootElement.GetProperty("authorization_servers").EnumerateArray()).GetString());
    }

    [Theory]
    [InlineData(false, false, "")]
    [InlineData(true, false, "")]
    [InlineData(true, true, "")]
    [InlineData(true, false, "/ngb")]
    public async Task PublicAddressAndApiTokenValidationRemainCanonicalBehindATrustedProxy(bool trustProxy, bool resourceOverride, string prefix)
    {
        const string publicOrigin = "https://api.example";
        var resource = publicOrigin + prefix + "/mcp";
        var settings = new Dictionary<string, string?>
        {
            ["Ngb:Hosting:PublicApiUrl"] = resourceOverride ? "https://other-api.example" : publicOrigin + prefix,
            ["Mcp:AllowLocalhostHttp"] = "true"
        };
        if (resourceOverride)
            settings["Mcp:Resource"] = resource;

        await using var host = await TestHost.StartAsync(settings: settings, trustProxy: trustProxy, pathBase: prefix);
        using var http = ProtocolHttp(host);
        http.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        http.DefaultRequestHeaders.Add("X-Forwarded-Host", "api.example");
        using var request = ToolRequest(host, "ngb_get_capabilities");
        using var response = await http.SendAsync(request);
        Assert.Equal(trustProxy ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);

        http.DefaultRequestHeaders.Authorization = null;
        using var challenge = await http.PostAsync(host.Resource, new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, challenge.StatusCode);
        Assert.Contains(publicOrigin + "/.well-known/oauth-protected-resource" + prefix + "/mcp", challenge.Headers.WwwAuthenticate.ToString());

        using var metadataResponse = await http.GetAsync(host.Origin + "/.well-known/oauth-protected-resource" + prefix + "/mcp");
        Assert.Equal(trustProxy ? HttpStatusCode.OK : HttpStatusCode.NotFound, metadataResponse.StatusCode);
        if (trustProxy)
        {
            using var metadata = JsonDocument.Parse(await metadataResponse.Content.ReadAsStringAsync());
            Assert.Equal(resource, metadata.RootElement.GetProperty("resource").GetString());

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token("audience"));
            using var wrongAudience = ToolRequest(host, "ngb_get_capabilities");
            using var rejected = await http.SendAsync(wrongAudience);
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        }
    }

    [Fact]
    public async Task DisabledMcpDoesNotRegisterAnEndpoint()
    {
        await using var host = await TestHost.StartAsync(enabled: false);
        using var client = new HttpClient();
        using var response = await client.PostAsync(host.Resource, new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FeatureDiscoveryAndEndpointKeepTheirStartupStateAfterConfigurationReload(bool enabled)
    {
        await using var host = await TestHost.StartAsync(enabled: enabled,
            configure: services => services.RemoveAll<INgbFeatureService>(), reloadFeatureAfterRegistration: true);
        await using var scope = host.Services.CreateAsyncScope();
        var features = scope.ServiceProvider.GetRequiredService<INgbFeatureService>();
        Assert.Equal(enabled, await features.IsEnabledAsync(NgbFeatures.Mcp, default));
        Assert.Contains(await features.GetAllAsync(default), feature => feature.Code == NgbFeatures.Mcp && feature.Enabled == enabled);

        using var http = new HttpClient();
        using var response = await http.PostAsync(host.Resource, new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(enabled ? HttpStatusCode.Unauthorized : HttpStatusCode.NotFound, response.StatusCode);
        using var metadata = await http.GetAsync(host.Origin + "/.well-known/oauth-protected-resource/mcp");
        Assert.Equal(enabled ? HttpStatusCode.OK : HttpStatusCode.NotFound, metadata.StatusCode);
    }

    [Fact]
    public async Task RateLimitAndPayloadBudgetRejectRequestsBeforeProtocolDispatch()
    {
        await using var host = await TestHost.StartAsync(settings: new Dictionary<string, string?>
        {
            ["Mcp:RequestsPerMinute"] = "2",
            ["Mcp:MaxRequestBytes"] = "1024"
        });
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token());
        using var oversized = await client.PostAsync(host.Resource, new StringContent(new string('x', 1025), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        using var malformed = await client.PostAsync(host.Resource, new StringContent("{", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        using var limited = await client.PostAsync(host.Resource, new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
    }

    [Fact]
    public async Task RecreatedTransportPoliciesRetainOriginsAndCurrentUserConstraints()
    {
        await using var host = await TestHost.StartAsync(settings: new Dictionary<string, string?>
        {
            ["Mcp:AllowedOrigins:0"] = "https://client.example"
        });
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "alex-carter"),
            new Claim("typ", "Bearer"),
            new Claim("auth_time", "1")
        ], "Bearer"));
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer test-token";
        for (var index = 0; index < 2; index++)
        {
            var cors = host.Services.GetRequiredService<IOptionsFactory<CorsOptions>>().Create(Options.DefaultName);
            var policy = cors.GetPolicy("NgbMcp.CurrentUser")!;
            Assert.Equal(new[] { "https://client.example", host.Origin }, policy.Origins);
            Assert.Contains("Mcp-Method", policy.Headers);
            var authorization = host.Services.GetRequiredService<IOptionsFactory<AuthorizationOptions>>().Create(Options.DefaultName);
            var requirement = authorization.GetPolicy("NgbMcp.CurrentUser")!;
            var service = host.Services.GetRequiredService<IAuthorizationService>();
            Assert.True((await service.AuthorizeAsync(user, http, requirement)).Succeeded);
            Assert.False((await service.AuthorizeAsync(new ClaimsPrincipal(), null, requirement)).Succeeded);
        }
    }

    [Fact]
    public async Task ExistingBearerValidationCanRejectThePrincipal()
    {
        await using var host = await TestHost.StartAsync(configure: services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Events.OnTokenValidated = context =>
                {
                    context.Principal = null;
                    context.Fail("Host policy rejected the principal.");
                    return Task.CompletedTask;
                };
            }));
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token());
        using var response = await client.PostAsync(host.Resource, new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CorsAllowsOnlyConfiguredOriginsAndExposesBearerChallenge()
    {
        await using var host = await TestHost.StartAsync(settings: new Dictionary<string, string?>
        {
            ["Mcp:AllowedOrigins:0"] = "https://client.example"
        });
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, host.Resource);
        request.Headers.Add("Origin", "https://client.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "Authorization, MCP-Protocol-Version, Mcp-Method, Mcp-Name");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("https://client.example", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        var allowedHeaders = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers"));
        Assert.Contains("mcp-method", allowedHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mcp-name", allowedHeaders, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("cursor", "invalid_cursor")]
    [InlineData("argument", "invalid_input")]
    [InlineData("forbidden", "unavailable")]
    [InlineData("json", "internal_error")]
    [InlineData("large-error", "internal_error")]
    public async Task ToolErrorsAreClassifiedAndDoNotExposeHandlerDiagnostics(string mode, string code)
    {
        await using var host = await TestHost.StartAsync(configure: services =>
        {
            services.AddNgbMcpContributor<TransportContributor>();
            services.AddScoped<FaultHandler>();
            services.AddScoped<WaitHandler>();
            services.AddSingleton<WaitState>();
        });
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(host.Resource),
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + host.Token() }
        }));
        var response = await client.CallToolAsync("custom_fault", new Dictionary<string, object?> { ["mode"] = mode });
        Assert.True(response.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(response.Content)).Text;
        using var error = JsonDocument.Parse(text);
        Assert.Equal(code, error.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("secret-database", text);
        Assert.InRange(text.Length, 1, 600);
    }

    [Fact]
    public async Task ConcurrentRequestsAreBoundedAndCancelledReadsReleaseTheirSlot()
    {
        var state = new WaitState();
        await using var host = await TestHost.StartAsync(settings: new Dictionary<string, string?>
        {
            ["Mcp:ConcurrentRequests"] = "1"
        }, configure: services =>
        {
            services.AddNgbMcpContributor<TransportContributor>();
            services.AddScoped<FaultHandler>();
            services.AddScoped<WaitHandler>();
            services.AddSingleton(state);
        });
        using var http = ProtocolHttp(host);
        using var cancelled = new CancellationTokenSource();
        using var waitingRequest = ToolRequest(host, "custom_wait");
        var pending = http.SendAsync(waitingRequest, cancelled.Token);
        var started = await Task.WhenAny(state.Started.Task, pending).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(state.Started.Task, started);
        using var limitedRequest = ToolRequest(host, "ngb_get_capabilities");
        using var limited = await http.SendAsync(limitedRequest);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await state.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await state.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var nextRequest = ToolRequest(host, "ngb_get_capabilities");
        using var next = await http.SendAsync(nextRequest);
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    private static HttpClient ProtocolHttp(TestHost host)
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token());
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        http.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
        http.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2026-07-28");
        return http;
    }

    [Fact]
    public async Task RequestDeadlineCancelsTheHandlerAndReleasesAdmission()
    {
        var state = new WaitState();
        await using var host = await TestHost.StartAsync(settings: new Dictionary<string, string?>
        {
            ["Mcp:RequestTimeoutSeconds"] = "1",
            ["Mcp:ConcurrentRequests"] = "1"
        }, configure: services =>
        {
            services.AddNgbMcpContributor<TransportContributor>();
            services.AddScoped<FaultHandler>();
            services.AddScoped<WaitHandler>();
            services.AddSingleton(state);
        });
        using var http = ProtocolHttp(host);
        using var request = ToolRequest(host, "custom_wait");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        // The SDK has already started SSE. A deadline must abort that stream instead of
        // appending a second HTTP response or leaving the read running indefinitely.
        await Assert.ThrowsAsync<HttpRequestException>(() => response.Content.ReadAsStringAsync()
            .WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(state.Started.Task.IsCompletedSuccessfully);
        Assert.True(state.Cancelled.Task.IsCompletedSuccessfully);
        await state.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var nextRequest = ToolRequest(host, "ngb_get_capabilities");
        using var next = await http.SendAsync(nextRequest);
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    [Fact]
    public async Task MismatchedEndpointPathFailsBeforeServing()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestHost.StartAsync(settings: new Dictionary<string, string?>
        {
            ["Mcp:Resource"] = "https://ngb.example/wrong-path"
        }));
    }

    [Fact]
    public async Task InsecureIssuerFailsAtStartup()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestHost.StartAsync(configure: services =>
            services.AddSingleton(new KeycloakSettings { Issuer = "http://untrusted.example/issuer" })));
    }

    [Fact]
    public async Task ExistingBearerEventTypeCanRejectThePrincipal()
    {
        await using var host = await TestHost.StartAsync(configure: services =>
        {
            services.AddScoped<RejectingBearerEvents>();
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                options => options.EventsType = typeof(RejectingBearerEvents));
        });
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token());
        using var request = ToolRequest(host, "ngb_get_capabilities");
        using var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("resource_metadata", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task ConfiguredUiOriginSupportsTheExistingMappedUserIdentity()
    {
        await using var host = await TestHost.StartAsync(settings: new Dictionary<string, string?>
        {
            ["Mcp:PublicUiOrigin"] = "https://ui.example",
            ["Mcp:UiRoutes:report"] = "/reports/{typeCode}"
        });
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(host.Resource),
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + host.Token("mapped-subject") }
        }));
        var result = await client.CallToolAsync("ngb_get_capabilities");
        Assert.NotEqual(true, result.IsError);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("azp")]
    [InlineData("client-id")]
    public async Task ExistingApiUserTokenWorksForRestAndMcp(string scenario)
    {
        await using var host = await TestHost.StartAsync();
        using var http = ProtocolHttp(host);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token(scenario));
        Assert.Equal("alex-carter", await http.GetStringAsync(host.Origin + "/api/current-user"));
        using var request = ToolRequest(host, "ngb_get_capabilities");
        using var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Basic test-token")]
    [InlineData("Bearer")]
    [InlineData("not a header")]
    [InlineData("Bearer test-token")]
    public async Task ApiQueryTokenAuthenticationDoesNotAuthorizeMcp(string? header)
    {
        await using var host = await TestHost.StartAsync(configure: services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Events.OnMessageReceived = context =>
                {
                    context.Token = context.Request.Query["access_token"];
                    return Task.CompletedTask;
                };
            }));
        using var http = new HttpClient();
        using var request = ToolRequest(host, "ngb_get_capabilities");
        request.RequestUri = new Uri(host.Resource + "?access_token=" + Uri.EscapeDataString(host.Token()));
        if (header is not null)
            request.Headers.TryAddWithoutValidation("Authorization", header);
        using var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CustomApiBearerSchemeUsesItsRegisteredValidationAndEvents()
    {
        await using var host = await TestHost.StartAsync(settings: new Dictionary<string, string?>
        {
            ["Mcp:AuthenticationScheme"] = "CustomApi"
        }, configure: services =>
        {
            services.AddAuthentication().AddJwtBearer("CustomApi");
            services.AddOptions<JwtBearerOptions>("CustomApi").Configure<IServiceProvider>((options, provider) =>
            {
                var existing = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
                options.Configuration = existing.Configuration;
                options.MapInboundClaims = existing.MapInboundClaims;
                options.TokenValidationParameters = existing.TokenValidationParameters.Clone();
                options.Events = existing.Events;
            });
        });
        using var http = ProtocolHttp(host);
        using var request = ToolRequest(host, "ngb_get_capabilities");
        using var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("cookie")]
    public async Task NonBearerSchemeFailsAtStartup(string scheme)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestHost.StartAsync(settings: new Dictionary<string, string?>
        {
            ["Mcp:AuthenticationScheme"] = scheme
        }, configure: services => services.AddAuthentication().AddCookie("cookie")));
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("lifetime")]
    [InlineData("signature")]
    [InlineData("audience")]
    [InlineData("require-audience")]
    [InlineData("signed")]
    [InlineData("expiration")]
    [InlineData("forward-authenticate")]
    [InlineData("forward-default")]
    [InlineData("forward-selector")]
    public async Task UnsafeApiBearerConfigurationFailsAtStartup(string setting)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestHost.StartAsync(configure: services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var validation = options.TokenValidationParameters;
                switch (setting)
                {
                    case "issuer":
                        validation.ValidateIssuer = false;
                        break;
                    case "lifetime":
                        validation.ValidateLifetime = false;
                        break;
                    case "signature":
                        validation.ValidateIssuerSigningKey = false;
                        break;
                    case "audience":
                        validation.ValidateAudience = false;
                        break;
                    case "require-audience":
                        validation.RequireAudience = false;
                        break;
                    case "signed":
                        validation.RequireSignedTokens = false;
                        break;
                    case "expiration":
                        validation.RequireExpirationTime = false;
                        break;
                    case "forward-authenticate":
                        options.ForwardAuthenticate = "cookie";
                        break;
                    case "forward-default":
                        options.ForwardDefault = "cookie";
                        break;
                    case "forward-selector":
                        options.ForwardDefaultSelector = _ => "cookie";
                        break;
                }
            })));
    }

    [Theory]
    [InlineData(4096, "large-result", true)]
    [InlineData(2048, "small-result", false)]
    [InlineData(1, "small-result", true)]
    public async Task ProtocolEnvelopeAlsoHonorsTheResponseByteLimit(int maximumBytes, string mode, bool exceedsLimit)
    {
        await using var host = await TestHost.StartAsync(settings: new Dictionary<string, string?>
        {
            ["Mcp:Reads:MaxResponseBytes"] = maximumBytes.ToString()
        }, configure: services =>
        {
            services.AddNgbMcpContributor<TransportContributor>();
            services.AddScoped<FaultHandler>();
            services.AddScoped<WaitHandler>();
            services.AddSingleton<WaitState>();
        });
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(host.Resource),
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + host.Token() }
        }));
        var result = await client.CallToolAsync("custom_fault", new Dictionary<string, object?> { ["mode"] = mode });
        Assert.Equal(exceedsLimit, result.IsError == true);
        if (exceedsLimit)
            Assert.Contains("result_too_large", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        else
            Assert.True(JsonSerializer.SerializeToUtf8Bytes(result).Length <= maximumBytes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(McpReadOptions.MaximumSupportedJsonDepth)]
    public async Task HttpEnvelopeAllowsTheConfiguredArgumentDepthAndLargeBodyBudget(int maximumDepth)
    {
        await using var host = await TestHost.StartAsync(settings: new Dictionary<string, string?>
        {
            ["Mcp:Reads:MaxJsonDepth"] = maximumDepth.ToString(),
            ["Mcp:MaxRequestBytes"] = int.MaxValue.ToString()
        }, configure: services =>
        {
            services.AddNgbMcpContributor<TransportContributor>();
            services.AddScoped<FaultHandler>();
            services.AddScoped<WaitHandler>();
            services.AddSingleton<WaitState>();
        });
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(host.Resource),
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + host.Token() }
        }));
        using var value = JsonDocument.Parse(string.Concat(Enumerable.Repeat("{\"child\":", maximumDepth - 1))
            + "0" + new string('}', maximumDepth - 1));

        var result = await client.CallToolAsync("custom_fault", new Dictionary<string, object?>
        {
            ["mode"] = "small-result",
            ["value"] = value.RootElement
        });

        Assert.NotEqual(true, result.IsError);
    }

    [Fact]
    public async Task ToolCatalogThatExceedsTheConfiguredBudgetFailsBeforeServing()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => TestHost.StartAsync(settings: new Dictionary<string, string?>
        {
            ["Mcp:Reads:MaxTools"] = "1"
        }));

        Assert.Contains(nameof(McpReadOptions.MaxTools), error.Message);
    }

    [Fact]
    public async Task InvalidHostAndNestedDuplicateJsonAreRejectedBeforeToolsRun()
    {
        await using var host = await TestHost.StartAsync();
        using var http = ProtocolHttp(host);
        using var wrongHost = ToolRequest(host, "ngb_get_capabilities");
        wrongHost.Headers.Host = "untrusted.example";
        using var rejectedHost = await http.SendAsync(wrongHost);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedHost.StatusCode);
        using var duplicate = new StringContent("{\"params\":[{\"nested\":1,\"nested\":2}]}", Encoding.UTF8, "application/json");
        using var rejectedJson = await http.PostAsync(host.Resource, duplicate);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedJson.StatusCode);
        Assert.Equal("no-store", rejectedJson.Headers.CacheControl!.ToString());
    }

    private static HttpRequestMessage ToolRequest(TestHost host, string name)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, host.Resource);
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", name);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name,
                arguments = new { },
                _meta = new Dictionary<string, object>
                {
                    ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                    ["io.modelcontextprotocol/clientCapabilities"] = new { }
                }
            }
        }), Encoding.UTF8, "application/json");
        return request;
    }

    public sealed record FaultRequest(string Mode, JsonElement? Value = null);
    public sealed record TransportResult(bool Completed, string? Payload = null);
    public sealed record WaitRequest;

    public sealed class RejectingBearerEvents : JwtBearerEvents
    {
        public override Task TokenValidated(TokenValidatedContext context)
        {
            context.Fail("Host policy rejected the token without removing its principal.");
            return Task.CompletedTask;
        }
    }

    public sealed class WaitState
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class WaitHandler(WaitState state) : IMcpReadToolHandler<WaitRequest, TransportResult>
    {
        public async Task<TransportResult> ExecuteAsync(WaitRequest request, CancellationToken ct)
        {
            state.Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new TransportResult(true);
            }
            finally
            {
                state.Cancelled.TrySetResult();
            }
        }
    }

    public sealed class FaultHandler : IMcpReadToolHandler<FaultRequest, TransportResult>
    {
        public Task<TransportResult> ExecuteAsync(FaultRequest request, CancellationToken ct)
        {
            if (request.Mode == "large-result")
                return Task.FromResult(new TransportResult(true, new string('<', 320)));

            if (request.Mode == "small-result")
                return Task.FromResult(new TransportResult(true));

            throw (request.Mode switch
            {
                "cursor" => new NgbArgumentInvalidException("cursor", "secret-database"),
                "argument" => new NgbArgumentInvalidException("field", "secret-database"),
                "forbidden" => new NgbPermissionDeniedException(NgbSystemPermissions.AuditView),
                "json" => new JsonException("secret-database"),
                _ => new McpReadException("custom_error", new string('x', 1000) + "secret-database")
            });
        }
    }

    public sealed class TransportContributor : IMcpReadToolContributor
    {
        public void Contribute(IMcpReadToolRegistryBuilder builder)
        {
            builder.Add<FaultRequest, TransportResult, FaultHandler>("custom_fault", "Read an error fixture.", [NgbSystemPermissions.AuditView]);
            builder.Add<WaitRequest, TransportResult, WaitHandler>("custom_wait", "Read a cancellation fixture.", [NgbSystemPermissions.AuditView]);
        }
    }

    private sealed class TestHost(WebApplication app, string origin, string path) : IAsyncDisposable
    {
        private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes("ngb-test-only-signing-key-never-use-in-production-2026"));
        internal string Origin { get; } = origin;
        internal IServiceProvider Services => app.Services;
        internal string Resource => Origin + path;

        internal static async Task<TestHost> StartAsync(bool enabled = true, Dictionary<string, string?>? settings = null,
            Action<IServiceCollection>? configure = null, string pattern = "/mcp", bool trustProxy = false, string pathBase = "",
            bool reloadFeatureAfterRegistration = false, bool mapReadControllers = false, bool legacyReportController = false)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var origin = $"http://127.0.0.1:{port}";
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls(origin);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureManagement:Mcp"] = enabled.ToString(),
                ["Ngb:Hosting:PublicApiUrl"] = origin,
                ["KeycloakSettings:Issuer"] = origin + "/issuer",
                ["KeycloakSettings:RequireHttpsMetadata"] = "false",
                ["KeycloakSettings:ClientIds:0"] = "ngb-api",
                ["KeycloakSettings:ClientIds:1"] = "ngb-web",
                ["Mcp:AllowLocalhostHttp"] = "true"
            });
            if (settings is not null)
                builder.Configuration.AddInMemoryCollection(settings);
            builder.Services.AddKeycloak(builder.Configuration);
            builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.MapInboundClaims = false;
                options.Configuration = new OpenIdConnectConfiguration { Issuer = origin + "/issuer" };
                options.Configuration.SigningKeys.Add(Key);
                options.ConfigurationManager = null;
                options.TokenValidationParameters.IssuerSigningKey = Key;
                options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
            });
            builder.Services.AddAuthorization();
            builder.Services.AddCors();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ICurrentActorContext, HttpCurrentActorContext>();
            builder.Services.AddScoped<IPermissionSnapshotProvider, TestSnapshotProvider>();
            builder.Services.AddScoped<INgbAccessChecker, NgbAccessChecker>();
            McpTestServices.AddReads(builder.Services);
            configure?.Invoke(builder.Services);
            if (mapReadControllers)
            {
                builder.Services.AddControllers().ConfigureApplicationPartManager(manager =>
                {
                    manager.FeatureProviders.Clear();
                    manager.FeatureProviders.Add(new ReadControllersFeatureProvider(legacyReportController));
                });
            }
            builder.Services.AddNgbMcp(builder.Configuration);
            if (reloadFeatureAfterRegistration)
            {
                builder.Configuration["FeatureManagement:Mcp"] = (!enabled).ToString();
                ((IConfigurationRoot)builder.Configuration).Reload();
            }

            var app = builder.Build();
            if (trustProxy)
            {
                var forwarded = new ForwardedHeadersOptions
                {
                    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
                    ForwardLimit = 1
                };
                forwarded.KnownIPNetworks.Clear();
                forwarded.KnownProxies.Clear();
                forwarded.KnownProxies.Add(IPAddress.Loopback);
                forwarded.KnownProxies.Add(IPAddress.IPv6Loopback);
                forwarded.AllowedHosts.Add("api.example");
                app.UseForwardedHeaders(forwarded);
            }
            if (pathBase.Length > 0)
                app.UsePathBase(pathBase);

            app.Use(async (context, next) =>
            {
                try
                {
                    await next(context);
                }
                finally
                {
                    if (context.Request.Headers["Mcp-Name"] == "custom_wait")
                        context.RequestServices.GetRequiredService<WaitState>().Completed.TrySetResult();
                }
            });
            app.UseCors();
            app.UseAuthentication();
            app.UseAuthorization();
            if (mapReadControllers)
                app.MapControllers();
            app.MapGet("/api/current-user", (ClaimsPrincipal user) => user.FindFirst("sub")!.Value).RequireAuthorization();
            app.MapNgbMcp(pattern);
            await app.StartAsync();
            return new TestHost(app, origin, pathBase + pattern);
        }

        internal string Token(string scenario = "valid")
        {
            var claims = new List<Claim>
            {
                new("sub", "alex-carter"),
                new("name", "Alex Carter"),
                new("active", scenario == "inactive" ? "false" : "true")
            };
            if (scenario != "service")
                claims.Add(new Claim("auth_time", scenario == "auth-time" ? "0" : DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds().ToString()));
            if (scenario == "grant")
                claims.Add(new Claim("grant_type", "client_credentials"));
            if (scenario == "mapped-subject")
            {
                claims.RemoveAll(claim => claim.Type == "sub");
                claims.Add(new Claim(ClaimTypes.NameIdentifier, "alex-carter"));
            }
            if (scenario == "subject")
                claims.RemoveAll(claim => claim.Type == "sub");
            if (scenario == "service-account")
                claims.Add(new Claim("preferred_username", "service-account-client"));
            if (scenario != "missing-type")
                claims.Add(new Claim("typ", scenario == "id-token" ? "ID" : scenario == "refresh-token" ? "Refresh" : "Bearer"));
            if (scenario is "azp" or "unknown-client")
                claims.Add(new Claim("azp", scenario == "azp" ? "ngb-web" : "other-web"));
            if (scenario == "client-id")
                claims.Add(new Claim("client_id", "ngb-web"));
            var token = new JwtSecurityToken(
                issuer: Origin + (scenario == "issuer" ? "/wrong-issuer" : "/issuer"),
                audience: scenario is "audience" or "azp" or "client-id" or "unknown-client" ? "account" : "ngb-api",
                claims: claims,
                notBefore: DateTime.UtcNow.AddMinutes(scenario == "future" ? 2 : -10),
                expires: scenario == "expired" ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(scenario == "signature"
                    ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes("another-invalid-test-signature-key-2026")) : Key, SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private sealed class TestSnapshotProvider(ICurrentActorContext actor) : IPermissionSnapshotProvider
    {
        public Task<PermissionSnapshot> GetCurrentAsync(CancellationToken ct)
        {
            var current = actor.Current;
            return Task.FromResult(current is null ? PermissionSnapshot.Anonymous
                : new PermissionSnapshot(null, current.AuthSubject, true, current.IsActive, true, 0, []));
        }
    }
}
