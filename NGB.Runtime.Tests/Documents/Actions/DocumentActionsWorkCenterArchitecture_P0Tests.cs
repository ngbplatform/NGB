using System.Xml.Linq;
using FluentAssertions;
using NGB.Core.Documents.Actions;
using NGB.Metadata.Documents.Actions;
using NGB.Runtime.Documents.Actions;
using Xunit;

namespace NGB.Runtime.Tests.Documents.Actions;

public sealed class DocumentActionsWorkCenterArchitecture_P0Tests
{
    private static readonly string[] VerticalProjectMarkers =
    [
        "PropertyManagement",
        "Trade",
        "AgencyBilling",
        "CRM"
    ];

    [Fact]
    public void Platform_projects_do_not_reference_vertical_assemblies()
    {
        var root = FindRepositoryRoot();
        var platformProjects = new[]
        {
            "NGB.Core/NGB.Core.csproj",
            "NGB.Metadata/NGB.Metadata.csproj",
            "NGB.Definitions/NGB.Definitions.csproj",
            "NGB.Application.Abstractions/NGB.Application.Abstractions.csproj",
            "NGB.Persistence/NGB.Persistence.csproj",
            "NGB.PostgreSql/NGB.PostgreSql.csproj",
            "NGB.Runtime/NGB.Runtime.csproj",
            "NGB.Api/NGB.Api.csproj"
        };

        foreach (var relativePath in platformProjects)
        {
            var references = ReadProjectReferences(Path.Combine(root, relativePath));
            references.Should().NotContain(
                reference => VerticalProjectMarkers.Any(
                    marker => reference.Contains(marker, StringComparison.OrdinalIgnoreCase)),
                $"{relativePath} is a platform boundary");
        }
    }

    [Fact]
    public void Core_and_metadata_do_not_reference_runtime_persistence_postgres_or_api()
    {
        var root = FindRepositoryRoot();
        var forbiddenMarkers = new[] { "Runtime", "Persistence", "PostgreSql", ".Api" };

        foreach (var relativePath in new[]
                 {
                     "NGB.Core/NGB.Core.csproj",
                     "NGB.Metadata/NGB.Metadata.csproj"
                 })
        {
            var references = ReadProjectReferences(Path.Combine(root, relativePath));
            references.Should().NotContain(
                reference => forbiddenMarkers.Any(
                    marker => reference.Contains(marker, StringComparison.OrdinalIgnoreCase)),
                $"{relativePath} belongs below runtime and infrastructure");
        }
    }

    [Fact]
    public void PostgreSql_provider_depends_only_on_platform_contracts()
    {
        var root = FindRepositoryRoot();
        var references = ReadProjectReferences(
            Path.Combine(root, "NGB.PostgreSql", "NGB.PostgreSql.csproj"));

        references.Should().NotContain(
            reference => VerticalProjectMarkers.Any(
                marker => reference.Contains(marker, StringComparison.OrdinalIgnoreCase)));
        references.Should().NotContain(
            reference => reference.Contains("NGB.Runtime", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Metadata_and_core_contracts_do_not_expose_runtime_handler_types()
    {
        typeof(DocumentActionMetadata).Assembly
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name)
            .Should().NotContain(name => name != null
                && name.StartsWith("NGB.", StringComparison.OrdinalIgnoreCase)
                && (
                name.Contains(".Runtime", StringComparison.OrdinalIgnoreCase)
                || name.Contains("PostgreSql", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".Api", StringComparison.OrdinalIgnoreCase)));

        typeof(DocumentActionCode).Assembly
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name)
            .Should().NotContain(name => name != null
                && name.StartsWith("NGB.", StringComparison.OrdinalIgnoreCase)
                && (
                name.Contains(".Runtime", StringComparison.OrdinalIgnoreCase)
                || name.Contains("PostgreSql", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".Api", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Shared_UI_framework_has_no_vertical_application_imports()
    {
        var root = FindRepositoryRoot();
        var sourceRoot = Path.Combine(root, "ui", "ngb-ui-framework", "src");
        var source = Directory
            .EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(static path => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                                  || path.EndsWith(".vue", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText)
            .ToArray();

        foreach (var marker in new[]
                 {
                     "ngb-property-management-web",
                     "ngb-trade-web",
                     "ngb-agency-billing-web",
                     "ngb-crm-web"
                 })
        {
            source.Should().NotContain(
                text => text.Contains(marker, StringComparison.OrdinalIgnoreCase),
                "the platform UI must consume semantic extension points only");
        }
    }

    [Fact]
    public void Canonical_registry_and_dispatcher_are_unique_and_legacy_action_paths_are_absent()
    {
        var runtimeAssembly = typeof(DocumentActionRegistry).Assembly;
        runtimeAssembly.GetTypes()
            .Where(type => type.Name == nameof(DocumentActionRegistry))
            .Should().ContainSingle();
        runtimeAssembly.GetTypes()
            .Where(type => type.Name == nameof(DocumentActionDispatcher))
            .Should().ContainSingle();

        var root = FindRepositoryRoot();
        File.Exists(Path.Combine(
                root,
                "NGB.Application.Abstractions",
                "Services",
                "IDocumentUiEffectsContributor.cs"))
            .Should().BeFalse();
        File.Exists(Path.Combine(
                root,
                "NGB.Contracts",
                "Services",
                "DocumentDerivationActionDto.cs"))
            .Should().BeFalse();
        File.Exists(Path.Combine(
                root,
                "ui",
                "ngb-property-management-web",
                "src",
                "editor",
                "documentActions.ts"))
            .Should().BeFalse();
    }

    private static IReadOnlyList<string> ReadProjectReferences(string projectPath)
        => XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(static reference => !string.IsNullOrWhiteSpace(reference))
            .Select(static reference => reference!)
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
