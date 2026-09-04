using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Hosting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Architecture;

public sealed class BackendLayeringArchitectureTests
{
    [Fact]
    public void Shared_non_api_hosts_do_not_reference_the_api_surface()
    {
        var root = FindRepositoryRoot();

        foreach (var project in new[]
                 {
                     "NGB.BackgroundJobs/NGB.BackgroundJobs.csproj",
                     "NGB.Watchdog/NGB.Watchdog.csproj"
                 })
        {
            ReadReferences(root, project).Should().NotContain(
                reference => reference.Contains("NGB.Api", StringComparison.OrdinalIgnoreCase),
                $"{project} must consume the narrow hosting layer rather than API controllers");
        }
    }

    [Fact]
    public void Reusable_web_surfaces_do_not_select_the_runtime_host_lifecycle()
    {
        var root = FindRepositoryRoot();

        foreach (var project in new[]
                 {
                     "NGB.Api/NGB.Api.csproj",
                     "NGB.BackgroundJobs/NGB.BackgroundJobs.csproj"
                 })
        {
            ReadReferences(root, project).Should().NotContain(reference =>
                reference.Contains("NGB.Runtime.Hosting", StringComparison.OrdinalIgnoreCase));
        }

        ReadSources(root, "NGB.Api").Should().NotContain(source =>
            source.Contains("AddNgbRuntimeStartupValidation", StringComparison.Ordinal));
        ReadSources(root, "NGB.BackgroundJobs").Should().NotContain(source =>
            source.Contains("AddNgbRuntimeStartupValidation", StringComparison.Ordinal));
    }

    [Fact]
    public void Provider_neutral_hosting_does_not_depend_on_application_or_provider_layers()
    {
        var root = FindRepositoryRoot();
        var references = ReadReferences(root, "NGB.Hosting.AspNetCore/NGB.Hosting.AspNetCore.csproj");
        var forbidden = new[]
        {
            "NGB.Api",
            "NGB.Runtime",
            "NGB.Persistence",
            "NGB.PostgreSql",
            "NGB.BackgroundJobs",
            "NGB.Watchdog"
        };

        references.Should().NotContain(reference => forbidden.Any(marker =>
            reference.Contains(marker, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Health_response_serialization_dependency_is_owned_by_shared_hosting()
    {
        const string healthChecksUiClient = "AspNetCore.HealthChecks.UI.Client";
        var root = FindRepositoryRoot();

        ReadReferences(root, "NGB.Hosting.AspNetCore/NGB.Hosting.AspNetCore.csproj")
            .Should().ContainSingle(reference =>
                string.Equals(reference, healthChecksUiClient, StringComparison.OrdinalIgnoreCase));

        foreach (var project in new[]
                 {
                     "NGB.Api/NGB.Api.csproj",
                     "NGB.BackgroundJobs/NGB.BackgroundJobs.csproj",
                     "NGB.Watchdog/NGB.Watchdog.csproj"
                 })
        {
            ReadReferences(root, project).Should().NotContain(reference =>
                string.Equals(reference, healthChecksUiClient, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var directory in new[] { "NGB.Api", "NGB.BackgroundJobs", "NGB.Watchdog" })
        {
            ReadSources(root, directory).Should().NotContain(source =>
                source.Contains("HealthChecks.UI.Client", StringComparison.Ordinal)
                || source.Contains("UIResponseWriter", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Provider_specific_web_integrations_live_in_a_dedicated_aspnet_adapter()
    {
        var root = FindRepositoryRoot();
        var apiProject = XDocument.Load(Path.Combine(root, "NGB.Api/NGB.Api.csproj"));
        var apiReferences = apiProject.Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();

        apiReferences.Should().NotContain(reference =>
            reference.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("HealthChecks.NpgSql", StringComparison.OrdinalIgnoreCase));

        var postgresReferences = ReadReferences(root, "NGB.PostgreSql/NGB.PostgreSql.csproj");
        postgresReferences.Should().NotContain(reference =>
            reference.Contains("AspNetCore", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("Diagnostics.HealthChecks", StringComparison.OrdinalIgnoreCase));
        ReadSources(root, "NGB.PostgreSql").Should().NotContain(source =>
            source.Contains("Microsoft.AspNetCore", StringComparison.Ordinal)
            || source.Contains("Microsoft.Extensions.Diagnostics.HealthChecks", StringComparison.Ordinal)
            || source.Contains("INgbExceptionHttpMapper", StringComparison.Ordinal));

        var adapterReferences = ReadReferences(
            root,
            "NGB.PostgreSql.AspNetCore/NGB.PostgreSql.AspNetCore.csproj");
        adapterReferences.Should().Contain(reference =>
            reference.Contains("NGB.Hosting.AspNetCore", StringComparison.OrdinalIgnoreCase));
        adapterReferences.Should().Contain(reference =>
            reference.Contains("NGB.PostgreSql", StringComparison.OrdinalIgnoreCase));

        File.Exists(Path.Combine(
                root,
                "NGB.PostgreSql.AspNetCore/ErrorHandling/PostgresExceptionHttpMapper.cs"))
            .Should().BeTrue();
        File.Exists(Path.Combine(root, "NGB.PostgreSql.AspNetCore/Health/PostgresHealthCheck.cs"))
            .Should().BeTrue();
        File.Exists(Path.Combine(root, "NGB.Tools/Exceptions/INgbExceptionHttpMapper.cs"))
            .Should().BeFalse();
    }

    [Fact]
    public void Application_services_do_not_own_sql_or_process_environment_configuration()
    {
        var root = FindRepositoryRoot();

        foreach (var relativeDirectory in new[]
                 {
                     "NGB.CRM.Runtime",
                     "NGB.PropertyManagement.Runtime"
                 })
        {
            var sources = ReadSources(root, relativeDirectory);
            sources.Should().NotContain(source => source.Contains("Dapper", StringComparison.Ordinal));
            sources.Should().NotContain(source => source.Contains("Npgsql", StringComparison.Ordinal));
            sources.Should().NotContain(source => source.Contains("GetEnvironmentVariable", StringComparison.Ordinal));
        }

        File.Exists(Path.Combine(
                root,
                "NGB.PropertyManagement.PostgreSql/Bootstrap/PropertyManagementSecuritySeeder.cs"))
            .Should().BeFalse();
    }

    [Fact]
    public void Runtime_does_not_choose_a_host_lifecycle_and_the_host_adapter_is_explicit()
    {
        var root = FindRepositoryRoot();
        var runtimeSources = ReadSources(root, "NGB.Runtime");

        runtimeSources.Should().NotContain(source => source.Contains("IHostedService", StringComparison.Ordinal));
        runtimeSources.Should().NotContain(source => source.Contains("BackgroundService", StringComparison.Ordinal));
        File.Exists(Path.Combine(root, "NGB.Runtime.Hosting/RuntimeHostingServiceCollectionExtensions.cs"))
            .Should().BeTrue();

        var services = new ServiceCollection();
        services.AddNgbRuntime();
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IHostedService));

        services.AddNgbRuntimeStartupValidation().AddNgbRuntimeStartupValidation();
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void Projection_contract_does_not_expose_a_transaction_or_raw_connection()
    {
        var root = FindRepositoryRoot();
        var contract = File.ReadAllText(Path.Combine(
            root,
            "NGB.Runtime/OperationalRegisters/Projections/OperationalRegisterMonthProjectionContext.cs"));

        contract.Should().NotContain("IUnitOfWork");
        contract.Should().NotContain("DbConnection");
        contract.Should().NotContain("IDbConnection");
    }

    [Fact]
    public void Api_internals_are_not_exposed_to_vertical_test_assemblies()
    {
        var root = FindRepositoryRoot();
        var friends = File.ReadAllText(Path.Combine(root, "NGB.Api/InternalsVisibleTo.cs"));

        friends.Should().NotContain("PropertyManagement");
        friends.Should().NotContain("AgencyBilling");
        friends.Should().NotContain("CRM");
        friends.Should().NotContain("Trade");
    }

    [Fact]
    public void Runtime_hosting_registration_guards_null_and_is_idempotent()
    {
        Action nullServices = () => RuntimeHostingServiceCollectionExtensions
            .AddNgbRuntimeStartupValidation(null!);
        nullServices.Should().Throw<NgbArgumentRequiredException>();

        var services = new ServiceCollection();
        services.AddNgbRuntimeStartupValidation().Should().BeSameAs(services);
        services.AddNgbRuntimeStartupValidation();
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(DefinitionsStartupValidatorHostedService));
    }

    private static IReadOnlyList<string> ReadReferences(string root, string relativeProject)
        => XDocument.Load(Path.Combine(root, relativeProject))
            .Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(reference => reference!)
            .ToArray();

    private static IReadOnlyList<string> ReadSources(string root, string relativeDirectory)
        => Directory.EnumerateFiles(
                Path.Combine(root, relativeDirectory),
                "*.cs",
                SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "NGB.sln")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the NGB repository root.");
    }
}
