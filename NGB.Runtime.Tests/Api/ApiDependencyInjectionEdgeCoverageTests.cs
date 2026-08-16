using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using NGB.Api;
using NGB.Api.CurrentUser;
using NGB.Api.Models;
using NGB.Api.Sso;
using NGB.Runtime.CurrentActor;
using NGB.Tools.Exceptions;
using Swashbuckle.AspNetCore.SwaggerGen;
using Xunit;

namespace NGB.Runtime.Tests.Api;

public sealed class ApiDependencyInjectionEdgeCoverageTests
{
    [Fact]
    public void Swagger_schema_ids_cover_plain_nested_generic_and_generic_parameter_types()
    {
        Invoke<string>("BuildSwaggerSchemaId", typeof(string)).Should().Be("System_String");
        Invoke<string>("BuildSwaggerSchemaId", typeof(Dictionary<string, List<int>>))
            .Should().Be("System_Collections_Generic_Dictionary_System_String_System_Collections_Generic_List_System_Int32");
        Invoke<string>("BuildSwaggerSchemaId", typeof(Nested)).Should().Contain("ApiDependencyInjectionEdgeCoverageTests_Nested");

        var genericParameter = typeof(Generic<>).GetGenericArguments()[0];
        Invoke<string>("BuildSwaggerSchemaId", genericParameter).Should().Be("T");
        Invoke<string>("SanitizeSwaggerSchemaId", "A.B+C[D],E").Should().Be("A_B_C_D__E");
    }

    [Fact]
    public void Swagger_tag_selector_prefers_group_then_controller_and_rejects_unknown_actions()
    {
        var grouped = new ApiDescription { GroupName = "group" };
        Invoke<IList<string>>("ResolveSwaggerTags", grouped).Should().Equal("group");

        var controller = new ApiDescription
        {
            ActionDescriptor = new ControllerActionDescriptor { ControllerName = "Documents" }
        };
        Invoke<IList<string>>("ResolveSwaggerTags", controller).Should().Equal("Documents");

        var unknown = new ApiDescription { ActionDescriptor = new ActionDescriptor() };
        Action act = () => Invoke<IList<string>>("ResolveSwaggerTags", unknown);
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<NgbInvariantViolationException>();

        Invoke<bool>("IncludeEveryHealthCheck", (object?)null).Should().BeTrue();
    }

    [Fact]
    public async Task Root_endpoint_returns_default_and_custom_context_values()
    {
        await AssertRoot("Web Application has been started.");
        await AssertRoot("custom context");
    }

    [Fact]
    public async Task Api_service_extensions_register_and_execute_options_swagger_health_and_http_client_callbacks()
    {
        var configuration = Configuration();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration, "Platform API");
        services.AddControllersApi();
        services.AddExternalLinks(configuration);
        services.AddHealthCheckHttpClient();
        services.AddHealthChecks()
            .AddWebApplication()
            .AddPostgres(configuration);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
        provider.GetRequiredService<KeycloakAdminClientSettings>().BaseUrl
            .Should().Be("https://identity.example");
        provider.GetRequiredService<KeycloakApiClientSettings>().Realm.Should().Be("platform");
        provider.GetRequiredService<ExternalLinksSettings>().Should().Be(
            new ExternalLinksSettings("/health-ui", "/jobs"));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(ICurrentActorContext)
            && descriptor.ImplementationType == typeof(HttpCurrentActorContext));

        provider.GetRequiredService<IOptions<MvcOptions>>().Value
            .SuppressImplicitRequiredAttributeForNonNullableReferenceTypes.Should().BeTrue();
        provider.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions.Converters
            .Should().ContainSingle(converter => converter is System.Text.Json.Serialization.JsonStringEnumConverter);
        provider.GetRequiredService<IOptions<CorsOptions>>().Value
            .GetPolicy("CompletelyAllowedCorsPolicy").Should().NotBeNull();

        var swagger = provider.GetRequiredService<IOptions<SwaggerGenOptions>>().Value;
        swagger.SchemaGeneratorOptions.SchemaIdSelector(typeof(List<int>))
            .Should().Be("System_Collections_Generic_List_System_Int32");
        swagger.SwaggerGeneratorOptions.DocInclusionPredicate("any", new ApiDescription()).Should().BeTrue();
        swagger.SwaggerGeneratorOptions.TagsSelector(new ApiDescription { GroupName = "group" })
            .Should().Equal("group");
        swagger.SwaggerGeneratorOptions.SecurityRequirements.Should().ContainSingle();
        swagger.SwaggerGeneratorOptions.SecurityRequirements.Single()(new OpenApiDocument())
            .Should().ContainSingle();

        using var healthClient = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient("HealthCheckHttpClient");
        var health = await provider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Name == "Web Application");
        health.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void Keycloak_admin_client_registration_uses_safe_defaults_when_the_section_is_absent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeycloakAdminClient(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<KeycloakAdminClientSettings>().Should().BeEquivalentTo(new
        {
            BaseUrl = string.Empty,
            Realm = string.Empty,
            ClientId = string.Empty,
            ClientSecret = string.Empty,
            AdminBatchConcurrency = 8
        });
        provider.GetRequiredService<KeycloakApiClientSettings>().Should().Be(
            new KeycloakApiClientSettings(string.Empty, string.Empty, string.Empty, string.Empty));
    }

    [Fact]
    public async Task Api_host_extensions_build_serilog_cors_swagger_and_health_middlewares()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Host.AddSerilog();
        builder.Services.AddLogging();
        builder.Services.AddInfrastructure(Configuration(), "Platform API");
        builder.Services.AddControllersApi();
        builder.Services.AddHealthChecks();
        var app = builder.Build();

        app.UseCompletelyAllowedCorsPolicy().Should().BeSameAs(app);
        app.UseSwagger("Platform API").Should().BeSameAs(app);
        app.UseHealthChecks().Should().BeSameAs(app);

        await app.DisposeAsync();
    }

    private static async Task AssertRoot(string expected)
    {
        var builder = WebApplication.CreateSlimBuilder();
        var app = builder.Build();
        app.MapRootEndpoint(expected);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(x => x.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(x => x.RoutePattern.RawText == "/");
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        (await reader.ReadToEndAsync()).Should().Be(expected);
        await app.DisposeAsync();
    }

    private static T Invoke<T>(string methodName, params object?[] arguments)
        => (T)(typeof(NGB.Api.DependencyInjection)
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, arguments)!);

    private static IConfiguration Configuration()
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["KeycloakSettings:Issuer"] = "https://identity.example/realms/platform",
            ["KeycloakSettings:ClientIds:0"] = "platform-web",
            ["KeycloakSettings:RequireHttpsMetadata"] = "false",
            ["KeycloakAdminClientSettings:BaseUrl"] = "https://identity.example",
            ["KeycloakAdminClientSettings:Realm"] = "platform",
            ["KeycloakAdminClientSettings:ClientId"] = "admin-client",
            ["KeycloakAdminClientSettings:ClientSecret"] = "secret",
            ["ExternalLinksSettings:HealthUiUrl"] = "/health-ui",
            ["ExternalLinksSettings:BackgroundJobsUiUrl"] = "/jobs",
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=platform;Username=test;Password=test"
        }).Build();

    private sealed class Nested;

    private sealed class Generic<T>;
}
