using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Moq;
using NGB.Contracts.Security;
using NGB.Core.Security;
using NGB.Persistence.Security;

namespace NGB.IntegrationTests.Security;

internal static class CurrentAccessHttpAssertions
{
    public static async Task VerifyAsync<TProgram>(
        WebApplicationFactory<TProgram> factory,
        bool platformUserExists,
        bool accountActive,
        string role)
        where TProgram : class
    {
        const string issuer = "https://example.invalid/realms/ngb";
        const string subject = "current-access-test";
        var userId = platformUserExists ? Guid.NewGuid() : (Guid?)null;
        var permissions = new Mock<IPermissionSnapshotRepository>(MockBehavior.Strict);
        permissions.Setup(x => x.GetUserAccessStateByAuthSubjectAsync(subject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId is { } id
                ? new PlatformUserAccessState(id, subject, null, "Administrator", accountActive, 1)
                : null);
        permissions.Setup(x => x.GetEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var roles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        roles.Setup(x => x.GetRolesForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        using var rsa = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(rsa) { KeyId = "current-access-test-key" };
        var identityConfiguration = new OpenIdConnectConfiguration { Issuer = issuer };
        identityConfiguration.SigningKeys.Add(signingKey);

        // Keep the application's routing, JWT validation, role mapping and access services.
        // Only identity discovery and persistence are isolated from external services.
        await using var configuredFactory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPermissionSnapshotRepository>();
            services.AddSingleton(permissions.Object);
            services.RemoveAll<IPlatformUserRoleRepository>();
            services.AddSingleton(roles.Object);
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(identityConfiguration);
            });
        }));
        using var client = configuredFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        using var anonymousResponse = await client.GetAsync("/api/security/me/access");
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: "ngb-api-tests",
            claims:
            [
                new Claim("sub", subject),
                new Claim("name", "Administrator"),
                new Claim("realm_access", $$"""{"roles":["{{role}}"]}""", JsonClaimValueTypes.Json)
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(token));

        using var response = await client.GetAsync("/api/security/me/access");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl?.NoStore.Should().BeTrue();
        var access = await response.Content.ReadFromJsonAsync<CurrentAccessDto>();
        access.Should().NotBeNull();
        access!.UserId.Should().Be(userId);
        access.AuthSubject.Should().Be(subject);
        access.IsAuthenticated.Should().BeTrue();
        access.IsActive.Should().Be(platformUserExists ? accountActive : role == "ngb-admin");
        access.IsBootstrapAdmin.Should().Be(role == "ngb-admin" && (!platformUserExists || accountActive));
        access.Permissions.Should().BeEmpty("administrator access must not depend on explicit grants");
        permissions.Verify(x => x.GetUserAccessStateByAuthSubjectAsync(subject, It.IsAny<CancellationToken>()), Times.Once);
    }
}
