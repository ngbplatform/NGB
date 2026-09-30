using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NGB.Api.Internal;
using NGB.Api.Models;
using NGB.Api.Reporting;
using NGB.Hosting.AspNetCore;
using NGB.Hosting.AspNetCore.Health;
using NGB.Hosting.AspNetCore.Identity;
using NGB.Contracts.Common;
using NGB.Runtime.CurrentActor;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Api;

public sealed class ApiInfrastructureFullCoverageTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task ExternalHealthCheck_uses_default_client_for_blank_name(string? clientName)
    {
        var factory = new RecordingHttpClientFactory(_ => Response(HttpStatusCode.OK));
        var sut = new TestHealthCheck(factory, "https://health.example/ready", "Example", clientName);

        var result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("Example is reachable and responding.");
        factory.RequestedNames.Should().Equal(string.Empty);
    }

    [Fact]
    public async Task ExternalHealthCheck_uses_named_client_and_reports_non_success_status()
    {
        var factory = new RecordingHttpClientFactory(_ => Response(HttpStatusCode.BadGateway));
        var sut = new TestHealthCheck(factory, "https://health.example/ready", "Example", "external");

        var result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Example URL returned status code BadGateway.");
        factory.RequestedNames.Should().Equal("external");
    }

    [Fact]
    public async Task ExternalHealthCheck_converts_transport_failures_to_unhealthy_result()
    {
        var factory = new RecordingHttpClientFactory(_ => throw new HttpRequestException("offline"));
        var sut = new TestHealthCheck(factory, "https://health.example/ready", "Example");

        var result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Error checking Example URL: offline");
    }

    [Fact]
    public async Task Keycloak_health_check_targets_the_issuer_discovery_document()
    {
        var factory = new RecordingHttpClientFactory(request =>
        {
            request.RequestUri.Should().Be(
                "https://identity.example/realms/platform/.well-known/openid-configuration");
            return Response(HttpStatusCode.OK);
        });
        var sut = new KeycloakHealthCheck(factory, new KeycloakSettings
        {
            Issuer = "https://identity.example/realms/platform"
        });

        var result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("Keycloak");
    }

    [Fact]
    public void QueryParsing_covers_defaults_invalid_numbers_reserved_keys_and_filters()
    {
        QueryParsing.ToPageRequest(new QueryCollection()).Should().BeEquivalentTo(new
        {
            Offset = 0,
            Limit = 50,
            Search = (string?)null,
            Filters = (IReadOnlyDictionary<string, string>?)null
        });

        var invalid = QueryParsing.ToPageRequest(Query(new Dictionary<string, string?>
        {
            ["offset"] = "not-an-int",
            ["LIMIT"] = "also-invalid",
            ["Search"] = "case-insensitive-lookup",
            ["state"] = "active"
        }));
        invalid.Offset.Should().Be(0);
        invalid.Limit.Should().Be(50);
        invalid.IncludeTotal.Should().BeFalse();
        invalid.Search.Should().Be("case-insensitive-lookup");
        invalid.Filters.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("state", "active"));

        var parsed = QueryParsing.ToPageRequest(Query(new Dictionary<string, string?>
        {
            ["offset"] = "-3",
            ["limit"] = "9999",
            ["search"] = "tenant",
            ["cursor"] = "opaque-cursor",
            ["includeTotal"] = "false",
            ["status"] = "open"
        }));
        parsed.Offset.Should().Be(0);
        parsed.Limit.Should().Be(PagingLimits.MaxPageSize);
        parsed.Search.Should().Be("tenant");
        parsed.Cursor.Should().Be("opaque-cursor");
        parsed.IncludeTotal.Should().BeFalse();
        parsed.Filters.Should().Contain("status", "open");

        QueryParsing.ToPageRequest(Query(new Dictionary<string, string?>
        {
            ["includeTotal"] = "true"
        })).IncludeTotal.Should().BeTrue();

        QueryParsing.ToPageRequest(Query(new Dictionary<string, string?>
        {
            ["includeTotal"] = "not-a-boolean"
        })).IncludeTotal.Should().BeFalse();

        var bounded = QueryParsing.ToPageRequest(Query(new Dictionary<string, string?>
        {
            ["offset"] = int.MaxValue.ToString(),
            ["limit"] = "0"
        }));
        bounded.Offset.Should().Be(PagingLimits.MaxOffset);
        bounded.Limit.Should().Be(PagingLimits.DefaultPageSize);

        PagingLimits.BoundOffset(-1).Should().Be(0);
        PagingLimits.BoundOffset(17).Should().Be(17);
        PagingLimits.BoundOffset(int.MaxValue).Should().Be(PagingLimits.MaxOffset);
    }

    [Fact]
    public void AdminConsoleOptions_normalize_blank_paths_slashes_and_valid_origins()
    {
        var blank = new AdminConsoleAuthOptions
        {
            CallbackPath = " ",
            PublicOrigin = null,
            ForceHttpsRedirectUri = false
        };
        blank.ValidateAndNormalize();
        blank.CallbackPath.Should().BeNull();
        blank.PublicOrigin.Should().BeNull();
        blank.ForceHttpsRedirectUri.Should().BeFalse();

        var root = new AdminConsoleAuthOptions { CallbackPath = "/", PublicOrigin = " http://localhost:5051/// " };
        root.ValidateAndNormalize();
        root.CallbackPath.Should().Be("/");
        root.PublicOrigin.Should().Be("http://localhost:5051");

        var relative = new AdminConsoleAuthOptions
        {
            CallbackPath = "signin-oidc/",
            PublicOrigin = "https://admin.example.com/"
        };
        relative.ValidateAndNormalize();
        relative.CallbackPath.Should().Be("/signin-oidc");
        relative.PublicOrigin.Should().Be("https://admin.example.com");
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("ftp://admin.example.com")]
    [InlineData("///")]
    public void AdminConsoleOptions_reject_invalid_public_origins(string origin)
    {
        var sut = new AdminConsoleAuthOptions { PublicOrigin = origin };

        Action act = sut.ValidateAndNormalize;

        var error = act.Should().Throw<NgbConfigurationViolationException>().Which;
        error.Context.Should().Contain("publicOrigin", origin);
    }

    [Fact]
    public void ReportAccessContext_and_security_key_parameters_cover_present_and_absent_actor()
    {
        var actorContext = new MutableActorContext();
        var sut = new HttpReportVariantAccessContext(actorContext);

        sut.AuthSubject.Should().BeNull();
        sut.Email.Should().BeNull();
        sut.DisplayName.Should().BeNull();
        sut.IsActive.Should().BeFalse();

        actorContext.Current = new ActorIdentity("subject", "user@example.com", "User", true);
        sut.AuthSubject.Should().Be("subject");
        sut.Email.Should().Be("user@example.com");
        sut.DisplayName.Should().Be("User");
        sut.IsActive.Should().BeTrue();

        new SecurityKeyParameters("AQAB", "modulus").Should().BeEquivalentTo(new
        {
            Exponent = "AQAB",
            Modulus = "modulus"
        });
    }

    [Fact]
    public void Configuration_and_api_setting_records_bind_and_expose_their_complete_contracts()
    {
        var values = new Dictionary<string, string?>
        {
            ["KeycloakSettings:Issuer"] = "https://issuer.example",
            ["KeycloakSettings:ClientIds:0"] = "web",
            ["KeycloakSettings:RequireHttpsMetadata"] = "false"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var settings = ConfigurationTools.GetSettings<KeycloakSettings>(configuration);
        settings.Issuer.Should().Be("https://issuer.example");
        settings.ClientIds.Should().Equal("web");
        settings.RequireHttpsMetadata.Should().BeFalse();

        new ExternalLinksSettings("/health", "/jobs").Should().BeEquivalentTo(new
        {
            HealthUiUrl = "/health",
            BackgroundJobsUiUrl = "/jobs"
        });
        new KeycloakApiClientSettings("https://id.example", "realm", "client", "secret")
            .Should().BeEquivalentTo(new
            {
                Url = "https://id.example",
                Realm = "realm",
                ClientId = "client",
                ClientSecret = "secret"
            });
    }

    private static IQueryCollection Query(IReadOnlyDictionary<string, string?> values)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = QueryString.Create(values);
        return context.Request.Query;
    }

    private static HttpResponseMessage Response(HttpStatusCode statusCode) => new(statusCode)
    {
        Content = new StringContent(string.Empty)
    };

    private sealed class TestHealthCheck(
        IHttpClientFactory factory,
        string url,
        string name,
        string? clientName = null)
        : BaseHttpExternalHealthCheck(factory, url, name, clientName);

    private sealed class RecordingHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : IHttpClientFactory
    {
        public List<string> RequestedNames { get; } = [];

        public HttpClient CreateClient(string name)
        {
            RequestedNames.Add(name);
            return new HttpClient(new StubHandler(responseFactory));
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(responseFactory(request));
    }

    private sealed class MutableActorContext : ICurrentActorContext
    {
        public ActorIdentity? Current { get; set; }
    }
}
