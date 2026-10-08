using NGB.Hosting.AspNetCore.Health;

namespace NGB.Hosting.AspNetCore.Identity;

public class KeycloakHealthCheck(IHttpClientFactory httpClientFactory, KeycloakSettings settings)
    : BaseHttpExternalHealthCheck(
        httpClientFactory,
        settings.MetadataAddress ?? settings.Issuer + "/.well-known/openid-configuration",
        "Keycloak");
