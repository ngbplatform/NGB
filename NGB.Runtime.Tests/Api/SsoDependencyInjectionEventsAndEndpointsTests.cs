using System.Security.Claims;
using System.Security.Principal;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Moq;
using NGB.Api.Sso;
using Xunit;
using JwtAuthenticationFailedContext = Microsoft.AspNetCore.Authentication.JwtBearer.AuthenticationFailedContext;
using JwtMessageReceivedContext = Microsoft.AspNetCore.Authentication.JwtBearer.MessageReceivedContext;
using JwtTokenValidatedContext = Microsoft.AspNetCore.Authentication.JwtBearer.TokenValidatedContext;
using OidcRedirectContext = Microsoft.AspNetCore.Authentication.OpenIdConnect.RedirectContext;
using OidcTokenValidatedContext = Microsoft.AspNetCore.Authentication.OpenIdConnect.TokenValidatedContext;

namespace NGB.Runtime.Tests.Api;

public sealed class SsoDependencyInjectionEventsAndEndpointsTests
{
    [Fact]
    public async Task Jwt_events_cover_signalr_token_extraction_logging_and_role_enrichment_guards()
    {
        using var provider = JwtServices();
        var authorization = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        var adminPolicy = authorization.GetPolicy("AuthAdminPolicy");
        adminPolicy.Should().NotBeNull();
        adminPolicy!.Requirements.OfType<ClaimsAuthorizationRequirement>()
            .Should().ContainSingle().Which.ClaimType.Should().Be(ClaimTypes.Role);
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme,
            JwtBearerDefaults.AuthenticationScheme,
            typeof(JwtBearerHandler));

        var matching = Http(provider, "/hubs/work-center", "?access_token=query-token");
        var matchingContext = new JwtMessageReceivedContext(matching, scheme, options);
        await options.Events.OnMessageReceived(matchingContext);
        matchingContext.Token.Should().Be("query-token");

        var existing = Http(provider, "/hubs/work-center", "?access_token=query-token");
        var existingContext = new JwtMessageReceivedContext(existing, scheme, options) { Token = "header-token" };
        await options.Events.OnMessageReceived(existingContext);
        existingContext.Token.Should().Be("header-token");

        var noQuery = new JwtMessageReceivedContext(Http(provider, "/hubs/work-center"), scheme, options);
        await options.Events.OnMessageReceived(noQuery);
        noQuery.Token.Should().BeNull();

        var wrongPath = new JwtMessageReceivedContext(
            Http(provider, "/api/test", "?access_token=query-token"), scheme, options);
        await options.Events.OnMessageReceived(wrongPath);
        wrongPath.Token.Should().BeNull();

        var failed = new JwtAuthenticationFailedContext(Http(provider, "/api/test"), scheme, options)
        {
            Exception = new InvalidOperationException("invalid token")
        };
        await options.Events.OnAuthenticationFailed(failed);

        var identity = new ClaimsIdentity(
            [new Claim("realm_access", "{\"roles\":[\"admin\"]}")], "jwt");
        var validated = new JwtTokenValidatedContext(Http(provider, "/api/test"), scheme, options)
        {
            Principal = new ClaimsPrincipal(identity)
        };
        await options.Events.OnTokenValidated(validated);
        identity.HasClaim(ClaimTypes.Role, "admin").Should().BeTrue();

        var customIdentity = new CustomIdentity();
        var custom = new JwtTokenValidatedContext(Http(provider, "/api/test"), scheme, options)
        {
            Principal = new ClaimsPrincipal(customIdentity)
        };
        await options.Events.OnTokenValidated(custom);

        var noPrincipal = new JwtTokenValidatedContext(Http(provider, "/api/test"), scheme, options);
        await options.Events.OnTokenValidated(noPrincipal);

        ReplayNamedOptionsConfiguration<JwtBearerOptions>(
            provider,
            JwtBearerDefaults.AuthenticationScheme);
    }

    [Fact]
    public async Task Admin_console_options_and_events_cover_callbacks_redirects_roles_and_signout_tokens()
    {
        using var publicProvider = AdminServices(options =>
        {
            options.CallbackPath = "custom-callback";
            options.PublicOrigin = "https://public.example/";
        });
        var publicOptions = OidcOptions(publicProvider);
        ReplayNamedOptionsConfiguration<OpenIdConnectOptions>(
            publicProvider,
            OpenIdConnectDefaults.AuthenticationScheme);
        var cookieOptions = publicProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        cookieOptions.SessionStore.Should().BeOfType<MemoryCacheTicketStore>();
        cookieOptions.SessionStore.Should().BeSameAs(
            publicProvider.GetRequiredService<MemoryCacheTicketStore>());
        cookieOptions.Cookie.Name.Should().Be(".ngb.admin-client.auth");
        cookieOptions.Cookie.MaxAge.Should().Be(TimeSpan.FromMinutes(60));
        cookieOptions.SlidingExpiration.Should().BeTrue();
        publicOptions.CallbackPath.Value.Should().Be("/custom-callback");
        publicOptions.CorrelationCookie.Path.Should().Be("/custom-callback");
        publicOptions.NonceCookie.Path.Should().Be("/custom-callback");
        publicOptions.ClientId.Should().Be("admin-client");
        publicOptions.Scope.Should().Contain(["openid", "profile", "email"]);

        var scheme = new AuthenticationScheme(
            OpenIdConnectDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme,
            typeof(OpenIdConnectHandler));
        var identity = new ClaimsIdentity([new Claim("roles", "admin")], "oidc");
        var validated = new OidcTokenValidatedContext(
            Http(publicProvider, "/signin-oidc"),
            scheme,
            publicOptions,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties());
        await publicOptions.Events.OnTokenValidated(validated);
        identity.HasClaim(ClaimTypes.Role, "admin").Should().BeTrue();

        var noClaimsIdentity = new OidcTokenValidatedContext(
            Http(publicProvider, "/signin-oidc"),
            scheme,
            publicOptions,
            new ClaimsPrincipal(new CustomIdentity()),
            new AuthenticationProperties());
        await publicOptions.Events.OnTokenValidated(noClaimsIdentity);

        var noPrincipal = new OidcTokenValidatedContext(
            Http(publicProvider, "/signin-oidc"),
            scheme,
            publicOptions,
            null!,
            new AuthenticationProperties());
        await publicOptions.Events.OnTokenValidated(noPrincipal);

        var publicRedirect = Redirect(publicProvider, scheme, publicOptions, "http://internal/signin-oidc");
        await publicOptions.Events.OnRedirectToIdentityProvider(publicRedirect);
        publicRedirect.ProtocolMessage.RedirectUri.Should().Be("https://public.example/custom-callback");

        var properties = new AuthenticationProperties { RedirectUri = "done" };
        properties.StoreTokens([new AuthenticationToken { Name = "id_token", Value = "stored-id-token" }]);
        var signOut = new OidcRedirectContext(
            Http(publicProvider, "/logout"), scheme, publicOptions, properties)
        {
            ProtocolMessage = new OpenIdConnectMessage()
        };
        await publicOptions.Events.OnRedirectToIdentityProviderForSignOut(signOut);
        signOut.ProtocolMessage.IssuerAddress.Should().EndWith("/protocol/openid-connect/logout");
        signOut.ProtocolMessage.IdTokenHint.Should().Be("stored-id-token");
        signOut.ProtocolMessage.PostLogoutRedirectUri.Should().Be("https://public.example/done");

        var noTokenAuthentication = new Mock<IAuthenticationService>(MockBehavior.Strict);
        noTokenAuthentication.Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                null))
            .ReturnsAsync(AuthenticateResult.NoResult());
        using var noTokenProvider = new ServiceCollection()
            .AddSingleton(noTokenAuthentication.Object)
            .BuildServiceProvider();
        var noPropertiesSignOut = new OidcRedirectContext(
            Http(noTokenProvider, "/logout"),
            scheme,
            publicOptions,
            null!)
        {
            ProtocolMessage = new OpenIdConnectMessage()
        };
        await publicOptions.Events.OnRedirectToIdentityProviderForSignOut(noPropertiesSignOut);
        noPropertiesSignOut.ProtocolMessage.IdTokenHint.Should().BeNull();
        noPropertiesSignOut.ProtocolMessage.PostLogoutRedirectUri.Should().BeNull();
        noTokenAuthentication.VerifyAll();

        using var httpsProvider = AdminServices(options => options.ForceHttpsRedirectUri = true);
        var httpsOptions = OidcOptions(httpsProvider);
        var httpsRedirect = Redirect(httpsProvider, scheme, httpsOptions, "http://internal/signin-oidc");
        await httpsOptions.Events.OnRedirectToIdentityProvider(httpsRedirect);
        httpsRedirect.ProtocolMessage.RedirectUri.Should().Be("https://internal/signin-oidc");

        var nullRedirect = Redirect(httpsProvider, scheme, httpsOptions, null);
        await httpsOptions.Events.OnRedirectToIdentityProvider(nullRedirect);
        nullRedirect.ProtocolMessage.RedirectUri.Should().BeNull();

        using var unchangedProvider = AdminServices(options => options.ForceHttpsRedirectUri = false);
        var unchangedOptions = OidcOptions(unchangedProvider);
        var unchanged = Redirect(unchangedProvider, scheme, unchangedOptions, "http://internal/signin-oidc");
        await unchangedOptions.Events.OnRedirectToIdentityProvider(unchanged);
        unchanged.ProtocolMessage.RedirectUri.Should().Be("http://internal/signin-oidc");

        using var defaultProvider = AdminServices();
        OidcOptions(defaultProvider).CallbackPath.Value.Should().Be("/signin-oidc");
    }

    [Fact]
    public async Task Account_endpoints_render_both_role_states_callbacks_denial_and_execute_signouts()
    {
        var auth = new Mock<IAuthenticationService>(MockBehavior.Strict);
        auth.Setup(x => x.SignOutAsync(
                It.IsAny<HttpContext>(),
                CookieAuthenticationDefaults.AuthenticationScheme,
                It.IsAny<AuthenticationProperties?>()))
            .Returns(Task.CompletedTask);
        auth.Setup(x => x.SignOutAsync(
                It.IsAny<HttpContext>(),
                OpenIdConnectDefaults.AuthenticationScheme,
                It.Is<AuthenticationProperties?>(p => p!.RedirectUri == "/logout-callback")))
            .Returns(Task.CompletedTask);
        using var requestServices = new ServiceCollection()
            .AddSingleton(auth.Object)
            .BuildServiceProvider();
        var builder = WebApplication.CreateSlimBuilder();
        var app = builder.Build();
        app.MapAccountEndpoints("/login");
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(x => x.Endpoints).ToArray();

        var anonymous = await Execute(endpoints, "account/logout", "GET", requestServices);
        anonymous.Body.Should().Contain("User has no role 'ngb-admin'");
        var admin = await Execute(
            endpoints,
            "account/logout",
            "GET",
            requestServices,
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "ngb-admin")], "test")));
        admin.Body.Should().Contain("User has role 'ngb-admin'");

        var callback = await Execute(endpoints, "/logout-callback", null, requestServices);
        callback.Body.Should().Contain("Sign-out successful").And.Contain("action='/login'");
        var denied = await Execute(endpoints, "/Account/AccessDenied", null, requestServices);
        denied.Body.Should().Be("Access Denied. You have no permissions.");

        var localLogout = await Execute(endpoints, "account/local-logout", "POST", requestServices);
        localLogout.StatusCode.Should().Be(204);
        await Execute(endpoints, "account/logout", "POST", requestServices);
        auth.VerifyAll();

        app.UseCustomForwardedHeaders().Should().BeSameAs(app);
        await app.DisposeAsync();
    }

    [Fact]
    public void Global_cookie_authorization_attaches_the_admin_policy_metadata()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddAuthorization();
        var app = builder.Build();
        app.MapGet("/protected", () => "ok").GlobalCookieRequireAuthorization();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(x => x.Endpoints).Single();
        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Should().ContainSingle(x => x.Policy == "AuthAdminPolicy");
    }

    private static ServiceProvider JwtServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks().AddKeycloak();
        services.AddKeycloak(Configuration());
        return services.BuildServiceProvider();
    }

    private static ServiceProvider AdminServices(Action<AdminConsoleAuthOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (configure is null)
            services.AddKeycloakForAdminConsole(Configuration());
        else
            services.AddKeycloakForAdminConsole(Configuration(), configure);
        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration()
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["KeycloakSettings:Issuer"] = "https://keycloak.example/realms/test",
            ["KeycloakSettings:ClientIds:0"] = "admin-client",
            ["KeycloakSettings:RequireHttpsMetadata"] = "false"
        }).Build();

    private static OpenIdConnectOptions OidcOptions(IServiceProvider provider)
        => provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);

    private static void ReplayNamedOptionsConfiguration<TOptions>(IServiceProvider provider, string name)
        where TOptions : class, new()
    {
        foreach (var configuration in provider.GetServices<IConfigureOptions<TOptions>>())
        {
            if (configuration is IConfigureNamedOptions<TOptions> named)
                named.Configure(name, new TOptions());
        }
    }

    private static OidcRedirectContext Redirect(
        IServiceProvider provider,
        AuthenticationScheme scheme,
        OpenIdConnectOptions options,
        string? redirectUri)
    {
        var context = new OidcRedirectContext(
            Http(provider, "/signin-oidc"), scheme, options, new AuthenticationProperties())
        {
            ProtocolMessage = new OpenIdConnectMessage { RedirectUri = redirectUri }
        };
        return context;
    }

    private static DefaultHttpContext Http(
        IServiceProvider services,
        string path,
        string? query = null)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = path;
        if (query is not null)
            context.Request.QueryString = new QueryString(query);
        return context;
    }

    private static async Task<(string Body, int StatusCode)> Execute(
        IReadOnlyList<Endpoint> endpoints,
        string route,
        string? method,
        IServiceProvider services,
        ClaimsPrincipal? principal = null)
    {
        var endpoint = endpoints.OfType<RouteEndpoint>().Single(candidate =>
            candidate.RoutePattern.RawText == route
            && (method is null
                || candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) == true));
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            User = principal ?? new ClaimsPrincipal(new ClaimsIdentity())
        };
        context.Response.Body = new MemoryStream();
        await endpoint.RequestDelegate!(context);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return (await reader.ReadToEndAsync(), context.Response.StatusCode);
    }

    private sealed class CustomIdentity : IIdentity
    {
        public string? AuthenticationType => "custom";
        public bool IsAuthenticated => true;
        public string? Name => "custom";
    }
}
