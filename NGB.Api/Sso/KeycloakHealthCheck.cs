using NGB.Api.Models;

namespace NGB.Api.Sso;

public class KeycloakHealthCheck(IHttpClientFactory httpClientFactory, KeycloakSettings settings)
    : BaseHttpExternalHealthCheck(
        httpClientFactory,
        settings.Issuer + "/.well-known/openid-configuration",
        "Keycloak");
