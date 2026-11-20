extern alias Worker;

using Worker::NgbApplication.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Xunit;

namespace NgbApplication.IntegrationTests;

public sealed class OperationalConsoleConfigurationTests
{
    [Theory]
    [InlineData("http://localhost:5184", false, "https://internal/health-ui?view=all")]
    [InlineData("http://127.0.0.1:5185", false, null)]
    [InlineData("https://operations.example.test", true, "https://internal/health-ui?view=all")]
    public async Task Public_origin_preserves_events_and_aligns_callback_logout_and_cookies(
        string origin, bool secure, string? logoutUri)
    {
        var redirects = 0;
        var signOuts = 0;
        var services = Services();
        services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            options.CallbackPath = "/identity/callback";
            options.Events.OnRedirectToIdentityProvider = context =>
            {
                redirects++;
                context.ProtocolMessage.RedirectUri = "https://internal/signin-oidc";
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToIdentityProviderForSignOut = context =>
            {
                signOuts++;
                context.ProtocolMessage.IdTokenHint = "preserved-token";
                context.ProtocolMessage.PostLogoutRedirectUri = logoutUri;
                return Task.CompletedTask;
            };
        });
        services.ConfigureOperationalConsole(origin);
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);
        var context = new RedirectContext(new DefaultHttpContext(),
            new AuthenticationScheme(OpenIdConnectDefaults.AuthenticationScheme, null, typeof(OpenIdConnectHandler)),
            options, new AuthenticationProperties())
        {
            ProtocolMessage = new OpenIdConnectMessage()
        };

        await options.Events.OnRedirectToIdentityProvider(context);
        Assert.Equal(origin + "/identity/callback", context.ProtocolMessage.RedirectUri);
        await options.Events.OnRedirectToIdentityProviderForSignOut(context);
        Assert.Equal(logoutUri is null ? null : origin + "/health-ui?view=all", context.ProtocolMessage.PostLogoutRedirectUri);
        Assert.Equal("preserved-token", context.ProtocolMessage.IdTokenHint);
        Assert.Equal(1, redirects);
        Assert.Equal(1, signOuts);
        var cookies = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        Assert.Equal(secure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest, cookies.Cookie.SecurePolicy);
        Assert.Equal(secure ? SameSiteMode.None : SameSiteMode.Lax, cookies.Cookie.SameSite);
        Assert.Equal(secure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest, options.CorrelationCookie.SecurePolicy);
        Assert.Equal(secure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest, options.NonceCookie.SecurePolicy);
        if (!secure)
            Assert.Equal("query", options.ResponseMode);
    }

    [Theory]
    [InlineData("http://operations.example.test")]
    [InlineData("https://user:secret@example.test")]
    [InlineData("https://example.test/health-ui")]
    [InlineData("https://example.test?origin=other")]
    [InlineData("https://example.test#fragment")]
    [InlineData("file:///tmp/dashboard")]
    [InlineData("ftp://operations.example.test")]
    [InlineData("not-an-origin")]
    public void Invalid_or_nonlocal_http_origins_fail_before_startup(string origin)
    {
        Assert.Throws<InvalidOperationException>(() => Services().ConfigureOperationalConsole(origin));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Unconfigured_origin_preserves_existing_host_options(string? origin)
    {
        var services = Services();
        services.ConfigureOperationalConsole(origin);
        await using var provider = services.BuildServiceProvider();
        var cookies = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        Assert.Equal(CookieSecurePolicy.Always, cookies.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.None, cookies.Cookie.SameSite);
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme);
        services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.None;
        });
        return services;
    }
}
