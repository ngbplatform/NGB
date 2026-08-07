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

    [Fact]
    public void Public_contracts_are_transport_only_and_do_not_depend_on_core()
    {
        var root = FindRepositoryRoot();
        var project = Path.Combine(root, "NGB.Contracts", "NGB.Contracts.csproj");

        ReadProjectReferences(project).Should().NotContain(
            reference => reference.Contains("NGB.Core", StringComparison.OrdinalIgnoreCase));
        ReadPackageReferences(project).Should().NotContain(
            reference => reference.Contains("NGB.Platform.Core", StringComparison.OrdinalIgnoreCase));
        ReadSources(root, "NGB.Contracts").Should().NotContain(
            source => source.Contains("using NGB.Core", StringComparison.Ordinal));
    }

    [Fact]
    public void Core_work_center_and_action_codes_do_not_expose_wire_json_or_ui_navigation()
    {
        var root = FindRepositoryRoot();
        var workCenter = ReadSources(root, "NGB.Core/WorkCenter");
        workCenter.Should().NotContain(source => source.Contains("System.Text.Json", StringComparison.Ordinal));
        workCenter.Should().NotContain(source => source.Contains("MetadataJson", StringComparison.Ordinal));
        workCenter.Should().NotContain(source => source.Contains("Navigation", StringComparison.Ordinal));

        var standardCodes = File.ReadAllText(Path.Combine(
            root,
            "NGB.Core/Documents/Actions/StandardDocumentActionCodes.cs"));
        standardCodes.Should().NotContain("ngb.document.action.completed");
        standardCodes.Should().NotContain("document.editor");
        standardCodes.Should().NotContain("documentId");
        standardCodes.Should().NotContain("documentType");
    }

    [Fact]
    public void Work_center_projection_boundary_is_typed_and_vertical_policies_do_not_parse_outbox_json()
    {
        var root = FindRepositoryRoot();
        var contract = File.ReadAllText(Path.Combine(
            root,
            "NGB.Application.Abstractions/Services/WorkCenter.cs"));
        contract.Should().Contain("IDocumentActionCompletedWorkCenterPolicy");
        contract.Should().Contain("HandleAsync(DocumentActionCompletedV1");
        contract.Should().NotContain("WorkCenterEventContext");
        contract.Should().NotContain("IWorkCenterEventPolicy");

        foreach (var relativeDirectory in new[]
                 {
                     "NGB.PropertyManagement.Runtime/WorkCenter",
                     "NGB.CRM.Runtime/WorkCenter"
                 })
        {
            var sources = ReadSources(root, relativeDirectory);
            sources.Should().NotContain(source => source.Contains("JsonDocument", StringComparison.Ordinal));
            sources.Should().NotContain(source => source.Contains("PayloadJson", StringComparison.Ordinal));
            sources.Should().NotContain(source => source.Contains("PlatformOutboxEvent", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Work_center_hosting_policy_belongs_to_api_and_is_registered_explicitly_by_every_host()
    {
        var root = FindRepositoryRoot();
        ReadSources(root, "NGB.Runtime/WorkCenter").Should().NotContain(
            source => source.Contains("BackgroundService", StringComparison.Ordinal));

        File.Exists(Path.Combine(root, "NGB.Api/WorkCenter/WorkCenterOutboxHostedService.cs"))
            .Should().BeTrue();

        foreach (var program in new[]
                 {
                     "NGB.PropertyManagement.Api/Program.cs",
                     "NGB.CRM.Api/Program.cs",
                     "NGB.AgencyBilling.Api/Program.cs",
                     "NGB.Trade.Api/Program.cs"
                 })
        {
            File.ReadAllText(Path.Combine(root, program))
                .Should().Contain("AddNgbWorkCenterOutboxProcessing(builder.Configuration)", $"{program} owns a projection worker");
        }

        var realtime = File.ReadAllText(Path.Combine(root, "NGB.Api/WorkCenter/WorkCenterRealtime.cs"));
        var realtimeMethod = realtime[
            realtime.IndexOf("AddNgbWorkCenterRealtime", StringComparison.Ordinal)..
            realtime.IndexOf("AddNgbWorkCenterOutboxProcessing", StringComparison.Ordinal)];
        realtimeMethod.Should().NotContain("IHostedService");
        realtimeMethod.Should().NotContain("WorkCenterOutboxHostedService");
    }

    [Fact]
    public void Api_health_adapter_does_not_reach_into_runtime_or_persistence()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "NGB.Api/WorkCenter/WorkCenterOutboxHealthCheck.cs"));

        source.Should().NotContain("using NGB.Persistence");
        source.Should().NotContain("using NGB.Runtime");
        source.Should().Contain("IWorkCenterOperationalHealthReader");
    }

    [Fact]
    public void Document_action_component_activation_has_one_explicit_composition_boundary()
    {
        var root = FindRepositoryRoot();
        var directory = Path.Combine(root, "NGB.Runtime/Documents/Actions");
        var sources = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Select(path => (Path: path, Source: File.ReadAllText(path)))
            .ToArray();

        var activationBoundaries = sources
            .Where(item => item.Source.Contains("IServiceProvider", StringComparison.Ordinal)
                           || item.Source.Contains("GetRequiredService", StringComparison.Ordinal)
                           || item.Source.Contains("GetService(", StringComparison.Ordinal))
            .ToArray();

        activationBoundaries.Should().ContainSingle();
        Path.GetFileName(activationBoundaries[0].Path).Should().Be("DocumentActionComponentResolver.cs");
        activationBoundaries[0].Source.Should().Contain("internal sealed class DocumentActionComponentResolver");
        activationBoundaries[0].Source.Should().Contain("NgbConfigurationViolationException");

        foreach (var orchestrator in new[]
                 {
                     "DocumentActionDispatcher.cs",
                     "DocumentActionEvaluator.cs"
                 })
        {
            var source = File.ReadAllText(Path.Combine(directory, orchestrator));
            source.Should().Contain("IDocumentActionComponentResolver");
            source.Should().NotContain("IServiceProvider");
            source.Should().NotContain("GetRequiredService");
            source.Should().NotContain("GetService(");
        }
    }

    [Fact]
    public void Interactive_document_lifecycle_has_one_dispatcher_path()
    {
        var root = FindRepositoryRoot();
        var services = File.ReadAllText(Path.Combine(
            root,
            "NGB.Application.Abstractions/Services/IDocumentService.cs"));
        var systemPortIndex = services.IndexOf("public interface IDocumentSystemLifecycleService", StringComparison.Ordinal);
        systemPortIndex.Should().BeGreaterThan(0);
        var interactivePort = services[..systemPortIndex];
        interactivePort.Should().NotContain("PostAsync(");
        interactivePort.Should().NotContain("UnpostAsync(");
        interactivePort.Should().NotContain("RepostAsync(");
        interactivePort.Should().NotContain("MarkForDeletionAsync(");
        interactivePort.Should().NotContain("UnmarkForDeletionAsync(");

        var controller = File.ReadAllText(Path.Combine(root, "NGB.Api/Controllers/DocumentControllerBase.cs"));
        controller.Should().Contain("actionDispatcher.ExecuteAsync(");
        controller.Should().NotContain("service.PostAsync(");
        controller.Should().NotContain("service.UnpostAsync(");
    }

    [Fact]
    public void Persistence_contracts_fail_at_compile_time_and_query_tabs_are_typed()
    {
        var root = FindRepositoryRoot();
        var documents = File.ReadAllText(Path.Combine(root, "NGB.Persistence/Documents/IDocumentRepository.cs"));
        documents.Should().NotContain("NotSupportedException");

        var workCenter = File.ReadAllText(Path.Combine(root, "NGB.Persistence/WorkCenter/IWorkCenterRepositories.cs"));
        workCenter.Should().Contain("WorkCenterQueryView View");
        workCenter.Should().NotContain("string? Tab");

        var contracts = File.ReadAllText(Path.Combine(root, "NGB.Contracts/WorkCenter/WorkCenterDtos.cs"));
        contracts.Should().Contain("WorkCenterTab Tab");
        contracts.Should().NotContain("string? Tab");
    }

    [Fact]
    public void Crm_runtime_declares_every_platform_package_it_uses_directly()
    {
        var root = FindRepositoryRoot();
        var packages = ReadPackageReferences(Path.Combine(root, "NGB.CRM.Runtime/NGB.CRM.Runtime.csproj"));

        packages.Should().Contain("NGB.Platform.Core");
        packages.Should().Contain("NGB.Platform.Persistence");
    }

    [Fact]
    public void Platform_work_center_baseline_has_no_generic_metadata_json_columns()
    {
        var root = FindRepositoryRoot();
        var migration = File.ReadAllText(Path.Combine(
            root,
            "NGB.PostgreSql/db/migrations/V2026_07_26_0100__ngb_platform_document_actions_work_center.sql"));
        migration.Should().NotContain("metadata_json");
    }

    private static IReadOnlyList<string> ReadProjectReferences(string projectPath)
        => XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(static reference => !string.IsNullOrWhiteSpace(reference))
            .Select(static reference => reference!)
            .ToArray();

    private static IReadOnlyList<string> ReadPackageReferences(string projectPath)
        => XDocument.Load(projectPath)
            .Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(static reference => !string.IsNullOrWhiteSpace(reference))
            .Select(static reference => reference!)
            .ToArray();

    private static IReadOnlyList<string> ReadSources(string root, string relativeDirectory)
        => Directory.EnumerateFiles(
                Path.Combine(root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar)),
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
