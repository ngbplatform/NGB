using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace NgbApplication.Hosting;

/// <summary>Application-owned public-origin configuration for Jobs and Watchdog hosts.</summary>
public static class OperationalConsoleConfiguration
{
    public static IServiceCollection ConfigureOperationalConsole(this IServiceCollection services, string? publicOrigin)
    {
        if (string.IsNullOrWhiteSpace(publicOrigin))
            return services;

        if (!Uri.TryCreate(publicOrigin, UriKind.Absolute, out var origin)
            || origin.UserInfo.Length != 0
            || origin.AbsolutePath != "/"
            || origin.Query.Length != 0
            || origin.Fragment.Length != 0
            || (origin.Scheme != Uri.UriSchemeHttps && (origin.Scheme != Uri.UriSchemeHttp || !origin.IsLoopback)))
        {
            throw new InvalidOperationException("Operations:PublicOrigin must be an HTTPS origin or an explicit loopback HTTP origin.");
        }

        var root = origin.GetLeftPart(UriPartial.Authority);
        var localHttp = origin.Scheme == Uri.UriSchemeHttp;
        services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            var redirect = options.Events.OnRedirectToIdentityProvider;
            options.Events.OnRedirectToIdentityProvider = async context =>
            {
                await redirect(context);
                context.ProtocolMessage.RedirectUri = root + options.CallbackPath;
            };

            var signOut = options.Events.OnRedirectToIdentityProviderForSignOut;
            options.Events.OnRedirectToIdentityProviderForSignOut = async context =>
            {
                await signOut(context);
                if (Uri.TryCreate(context.ProtocolMessage.PostLogoutRedirectUri, UriKind.Absolute, out var target))
                    context.ProtocolMessage.PostLogoutRedirectUri = root + target.PathAndQuery;
            };

            if (localHttp)
            {
                options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.NonceCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                // Lax cookies return on a top-level GET authorization-code callback.
                options.ResponseMode = "query";
            }
        });

        if (localHttp)
        {
            services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.Cookie.SameSite = SameSiteMode.Lax;
            });
        }

        return services;
    }
}
