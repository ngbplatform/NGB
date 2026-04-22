using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NGB.Api.Models;
using NGB.Tools.Exceptions;

namespace NGB.Api.Sso;

public static class DependencyInjection
{
    private const string AdminRole = "ngb-admin";
    private const string AuthAdminPolicy = "AuthAdminPolicy";
    private static readonly string[] KeycloakRoleClaimTypes = ["realm_access", "resource_access", "role", "roles"];

    public static IHealthChecksBuilder AddKeycloak(this IHealthChecksBuilder builder, string name = "Keycloak")
    {
        return builder.AddCheck<KeycloakHealthCheck>(name);
    }

    private static RsaSecurityKey GetSigningKey(SecurityKeyParameters parameters)
    {
        var exponent = Base64UrlEncoder.DecodeBytes(parameters.Exponent);
        var modulus = Base64UrlEncoder.DecodeBytes(parameters.Modulus);

        return new RsaSecurityKey(new RSAParameters
        {
            Exponent = exponent,
            Modulus = modulus
        });
    }

    private static void AddAuthorizationAuthAdminPolicy(IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthAdminPolicy, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(ClaimTypes.Role, AdminRole);
            });
        });
    }
    
    public static IServiceCollection AddKeycloak(this IServiceCollection services, IConfiguration configuration)
    {
        var keycloakSettings = ConfigurationTools.GetSettings<KeycloakSettings>(configuration);
        var validClientIds = GetValidClientIds(keycloakSettings);
        services.AddSingleton(keycloakSettings);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = keycloakSettings.Issuer;
                options.RequireHttpsMetadata = keycloakSettings.RequireHttpsMetadata;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateLifetime = true,
                    ValidateIssuer = true,
                    ValidIssuer = keycloakSettings.Issuer,
                    ValidateAudience = true,
                    ValidAudiences = validClientIds,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "preferred_username",
                    RoleClaimType = ClaimTypes.Role,
                    AudienceValidator = BuildKeycloakAudienceValidator(validClientIds),
                    // IssuerSigningKey = GetSigningKey(keycloakSettings.SecurityKeyParameters)
                };
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("NGB.Api.Sso.Keycloak")
                            .LogWarning(context.Exception, "Bearer token authentication failed.");

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        if (context.Principal?.Identity is ClaimsIdentity identity)
                        {
                            AddKeycloakRoleClaims(identity, validClientIds);
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        AddAuthorizationAuthAdminPolicy(services);

        return services;
    }

    public static IServiceCollection AddKeycloakForAdminConsole(this IServiceCollection services,
        IConfiguration configuration)
    {
        return services.AddKeycloakForAdminConsole(configuration, configure: null);
    }

    public static IServiceCollection AddKeycloakForAdminConsole(this IServiceCollection services,
        IConfiguration configuration,
        Action<AdminConsoleAuthOptions>? configure)
    {
        var keycloakSettings = ConfigurationTools.GetSettings<KeycloakSettings>(configuration);
        var validClientIds = GetValidClientIds(keycloakSettings);
        var primaryClientId = validClientIds[0];
        var cookiePrefix = BuildAdminConsoleCookiePrefix(primaryClientId);
        var adminConsoleAuthOptions = new AdminConsoleAuthOptions();
        configure?.Invoke(adminConsoleAuthOptions);
        adminConsoleAuthOptions.ValidateAndNormalize();

        services.AddSingleton(keycloakSettings);

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultSignOutScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.SessionStore = new MemoryCacheTicketStore();
                options.Cookie.Name = $"{cookiePrefix}.auth";
                options.Cookie.MaxAge = TimeSpan.FromMinutes(60);
                options.Cookie.SameSite = SameSiteMode.None;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.SlidingExpiration = true;

                #region For Debugging

                // options.Events.OnSigningIn = context =>
                // {
                //     foreach (var claim in context.Principal.Claims)
                //     {
                //         Console.WriteLine($"{claim.Type} - {claim.Value}");
                //     }
                //     return Task.CompletedTask;
                // };

                #endregion
            })
            .AddOpenIdConnect(options =>
            {
                var callbackPath = adminConsoleAuthOptions.CallbackPath ?? "/signin-oidc";

                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                
                options.Authority = keycloakSettings.Issuer;
                options.ClientId = primaryClientId;
                options.CallbackPath= callbackPath;
                options.SignedOutCallbackPath = "/logout-callback";
                options.RequireHttpsMetadata = keycloakSettings.RequireHttpsMetadata;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.ClaimActions.MapAll();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                options.SaveTokens = true;
                
                // Token response type, will sometimes need to be changed to IdToken, depending on config.
                options.ResponseType = OpenIdConnectResponseType.Code;
                
                // SameSite is needed for Chrome/Firefox, as they will give http error 500 back, if not set to unspecified.
                options.NonceCookie.SameSite = SameSiteMode.Unspecified;
                options.CorrelationCookie.SameSite = SameSiteMode.Unspecified;
                options.NonceCookie.Name = $"{cookiePrefix}.nonce";
                options.CorrelationCookie.Name = $"{cookiePrefix}.corr";
                options.NonceCookie.Path = callbackPath;
                options.CorrelationCookie.Path = callbackPath;
                
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateLifetime = true,
                    ValidateIssuer = true,
                    ValidIssuer = keycloakSettings.Issuer,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "preferred_username",
                    RoleClaimType = ClaimTypes.Role
                };

                options.Events = new OpenIdConnectEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (context.Principal?.Identity is ClaimsIdentity identity)
                            AddKeycloakRoleClaims(identity, validClientIds);

                        return Task.CompletedTask;
                    },

                    OnRedirectToIdentityProvider = context =>
                    {
                        if (!string.IsNullOrWhiteSpace(adminConsoleAuthOptions.PublicOrigin))
                        {
                            context.ProtocolMessage.RedirectUri = $"{adminConsoleAuthOptions.PublicOrigin}{callbackPath}";
                        }
                        else if (adminConsoleAuthOptions.ForceHttpsRedirectUri && context.ProtocolMessage.RedirectUri != null)
                        {
                            // Ensure HTTPS is used in redirect URIs
                            context.ProtocolMessage.RedirectUri = context.ProtocolMessage.RedirectUri.Replace("http://", "https://");
                        }

                        return Task.CompletedTask;
                    },

                    OnRedirectToIdentityProviderForSignOut = async context =>
                    {
                        var logoutUri = keycloakSettings.Issuer + "/protocol/openid-connect/logout";
                        context.ProtocolMessage.IssuerAddress = logoutUri;

                        var idToken = context.Properties?.GetTokenValue("id_token")
                                      ?? await context.HttpContext.GetTokenAsync("id_token");

                        if (!string.IsNullOrWhiteSpace(idToken))
                            context.ProtocolMessage.IdTokenHint = idToken;

                        var postLogoutRedirectUri = ResolveAbsoluteRedirectUri(
                            context.Properties?.RedirectUri,
                            context.Request.Scheme,
                            context.Request.Host,
                            adminConsoleAuthOptions.PublicOrigin);

                        if (!string.IsNullOrWhiteSpace(postLogoutRedirectUri))
                            context.ProtocolMessage.PostLogoutRedirectUri = postLogoutRedirectUri;
                    },
                };
            });

        AddAuthorizationAuthAdminPolicy(services);

        return services;
    }
    
    public static IEndpointConventionBuilder GlobalCookieRequireAuthorization(this IEndpointConventionBuilder builder)
    {
        return builder.RequireAuthorization(AuthAdminPolicy);
    }

    private static string[] GetValidClientIds(KeycloakSettings settings)
    {
        if (settings is null)
            throw new NgbArgumentRequiredException(nameof(settings));

        var clientIds = settings.ClientIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (clientIds.Length == 0)
            throw new NgbConfigurationViolationException("At least one Keycloak client id must be configured.");

        return clientIds;
    }

    private static AudienceValidator BuildKeycloakAudienceValidator(IReadOnlyCollection<string> validClientIds)
    {
        var allowedClientIds = new HashSet<string>(validClientIds, StringComparer.Ordinal);

        return (audiences, securityToken, _) =>
        {
            if (audiences?.Any(allowedClientIds.Contains) == true)
                return true;

            return TryReadSecurityTokenStringClaim(securityToken, "azp", out var authorizedParty)
                   && allowedClientIds.Contains(authorizedParty)
                || TryReadSecurityTokenStringClaim(securityToken, "client_id", out var clientId)
                   && allowedClientIds.Contains(clientId);
        };
    }

    private static bool TryReadSecurityTokenStringClaim(SecurityToken securityToken, string claimType, out string value)
    {
        if (securityToken is null)
            throw new NgbArgumentRequiredException(nameof(securityToken));

        value = string.Empty;

        if (securityToken is JwtSecurityToken jwt)
            return TryReadRawTokenValue(jwt.Payload.TryGetValue(claimType, out var jwtValue) ? jwtValue : null, out value);

        if (securityToken is JsonWebToken jsonWebToken)
        {
            if (jsonWebToken.TryGetClaim(claimType, out var claim) && !string.IsNullOrWhiteSpace(claim.Value))
            {
                value = claim.Value;
                return true;
            }

            if (jsonWebToken.TryGetPayloadValue<object>(claimType, out var payloadValue))
                return TryReadRawTokenValue(payloadValue, out value);

            return false;
        }

        var fallbackClaim = securityToken switch
        {
            JsonWebToken jwtToken => jwtToken.Claims.FirstOrDefault(x => x.Type == claimType),
            JwtSecurityToken jwtToken => jwtToken.Claims.FirstOrDefault(x => x.Type == claimType),
            _ => null
        };

        if (fallbackClaim is null || string.IsNullOrWhiteSpace(fallbackClaim.Value))
            return false;

        value = fallbackClaim.Value;
        return true;
    }

    private static bool TryReadRawTokenValue(object? rawValue, out string value)
    {
        value = rawValue switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
            _ => rawValue?.ToString() ?? string.Empty
        };

        return !string.IsNullOrWhiteSpace(value);
    }

    private static void AddKeycloakRoleClaims(ClaimsIdentity identity, IReadOnlyCollection<string> validClientIds)
    {
        if (identity is null)
            throw new NgbArgumentRequiredException(nameof(identity));

        var existingRoleClaims = new HashSet<string>(
            identity.FindAll(ClaimTypes.Role).Select(x => x.Value),
            StringComparer.OrdinalIgnoreCase);

        foreach (var role in ReadKeycloakRoles(identity, validClientIds))
        {
            if (existingRoleClaims.Add(role))
                identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }
    }

    private static IEnumerable<string> ReadKeycloakRoles(
        ClaimsIdentity identity,
        IReadOnlyCollection<string> validClientIds)
    {
        foreach (var claim in identity.Claims.Where(x => x.Type is "role" or "roles").ToArray())
        {
            if (!string.IsNullOrWhiteSpace(claim.Value))
                yield return claim.Value;
        }

        foreach (var claim in identity.Claims.Where(x => KeycloakRoleClaimTypes.Contains(x.Type, StringComparer.Ordinal)).ToArray())
        {
            if (string.IsNullOrWhiteSpace(claim.Value))
                continue;

            if (claim.Type == "realm_access")
            {
                foreach (var role in ReadKeycloakRolesFromRealmAccess(claim.Value))
                {
                    yield return role;
                }

                continue;
            }

            if (claim.Type == "resource_access")
            {
                foreach (var role in ReadKeycloakRolesFromResourceAccess(claim.Value, validClientIds))
                {
                    yield return role;
                }
            }
        }
    }

    private static IEnumerable<string> ReadKeycloakRolesFromRealmAccess(string json)
    {
        var roles = new List<string>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("roles", out var rolesElement) || rolesElement.ValueKind != JsonValueKind.Array)
                return roles;

            foreach (var role in ReadRoleArray(rolesElement))
            {
                roles.Add(role);
            }
        }
        catch (JsonException)
        {
        }

        return roles;
    }

    private static IEnumerable<string> ReadKeycloakRolesFromResourceAccess(
        string json,
        IReadOnlyCollection<string> validClientIds)
    {
        var roles = new List<string>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return roles;

            foreach (var clientId in validClientIds)
            {
                if (!document.RootElement.TryGetProperty(clientId, out var clientElement))
                    continue;

                if (!clientElement.TryGetProperty("roles", out var rolesElement) || rolesElement.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var role in ReadRoleArray(rolesElement))
                {
                    roles.Add(role);
                }
            }
        }
        catch (JsonException)
        {
        }

        return roles;
    }

    private static IEnumerable<string> ReadRoleArray(JsonElement rolesElement)
    {
        foreach (var roleElement in rolesElement.EnumerateArray())
        {
            if (roleElement.ValueKind != JsonValueKind.String)
                continue;

            var role = roleElement.GetString();
            if (!string.IsNullOrWhiteSpace(role))
                yield return role;
        }
    }

    public static void MapAccountEndpoints(this IEndpointRouteBuilder endpoints, string redirectUrl)
    {
        endpoints.MapPost("account/logout", async context =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignOutAsync(
                OpenIdConnectDefaults.AuthenticationScheme,
                new AuthenticationProperties { RedirectUri = "/logout-callback" });
        });

        endpoints.MapPost("account/local-logout", async context =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        endpoints.MapGet("account/logout", async context =>
        {
            var title = context.User.HasClaim(ClaimTypes.Role, AdminRole)
                ? $"User has role '{AdminRole}'"
                : $"User has no role '{AdminRole}'";

            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync($@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset='utf-8'>
                    <meta name='viewport' content='width=device-width, initial-scale=1'>
                    <title>Background Jobs</title>
                </head>
                <body>
                    <p>{title}</p>
                    <form method='post' action='/account/logout'>
                        <button type='submit'>Logout</button>
                    </form>
                </body>
                </html>");
        });

        endpoints.Map("/logout-callback", async context =>
        {
            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync($@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset='utf-8'>
                    <meta name='viewport' content='width=device-width, initial-scale=1'>
                    <title>Background Jobs</title>
                </head>
                <body>
                    <p>Sign-out successful</p>
                    <form method='get' action='{redirectUrl}'>
                        <button type='submit'>Login</button>
                    </form>
                </body>
                </html>");
        });

        endpoints.Map("/Account/AccessDenied", async context =>
        {
            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync("Access Denied. You have no permissions.");
        });
    }
    
    public static IApplicationBuilder UseCustomForwardedHeaders(this IApplicationBuilder app)
    {
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost,
            KnownIPNetworks = { }, // Trust all networks (use with caution in production)
            KnownProxies = { }   // Trust all proxies (use with caution in production)
        });

        return app;
    }

    private static string BuildAdminConsoleCookiePrefix(string clientId)
    {
        var normalized = new string(
            clientId
                .Trim()
                .ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
                .ToArray())
            .Trim('-');

        return string.IsNullOrWhiteSpace(normalized)
            ? ".ngb.admin-console"
            : $".ngb.{normalized}";
    }

    private static string? ResolveAbsoluteRedirectUri(
        string? configuredRedirectUri,
        string requestScheme,
        HostString requestHost,
        string? publicOrigin)
    {
        var value = string.IsNullOrWhiteSpace(configuredRedirectUri)
            ? null
            : configuredRedirectUri.Trim();

        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (Uri.TryCreate(value, UriKind.Absolute, out var absoluteUri))
            return absoluteUri.ToString();

        var normalizedPath = value.StartsWith('/') ? value : $"/{value}";
        var origin = !string.IsNullOrWhiteSpace(publicOrigin)
            ? publicOrigin.TrimEnd('/')
            : $"{requestScheme}://{requestHost.Value}";

        return $"{origin}{normalizedPath}";
    }
}
