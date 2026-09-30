using System.Xml.Linq;
using FluentAssertions;

namespace NGB.CRM.Runtime.Tests.Packaging;

public sealed class CrmPackageReferenceGuard_P0Tests
{
    [Fact]
    public void Crm_Projects_Do_Not_ProjectReference_Platform_Source_Projects()
    {
        var root = FindRepositoryRoot();
        var crmProjects = Directory.EnumerateDirectories(root, "NGB.CRM*", SearchOption.TopDirectoryOnly)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.csproj"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        crmProjects.Should().NotBeEmpty();

        var violations = new List<string>();
        foreach (var project in crmProjects)
        {
            var doc = XDocument.Load(project);
            var projectReferences = doc.Descendants("ProjectReference")
                .Select(x => x.Attribute("Include")?.Value)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToArray();

            foreach (var reference in projectReferences)
            {
                var normalizedReference = reference.Replace('\\', Path.DirectorySeparatorChar);
                var referencedProject = Path.GetFileNameWithoutExtension(normalizedReference);
                if (!referencedProject.StartsWith("NGB.CRM", StringComparison.Ordinal))
                    violations.Add($"{Path.GetFileName(project)} -> {reference}");
            }
        }

        violations.Should().BeEmpty("CRM may reference only other NGB.CRM projects; platform dependencies must be NuGet packages");
    }

    [Fact]
    public void Crm_Platform_PackageReferences_Use_Central_NgbPlatform_Version()
    {
        var root = FindRepositoryRoot();
        var buildProps = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
        var releaseVersion = buildProps.Descendants("Version").Single().Value;
        var platformVersion = buildProps.Descendants("NgbPlatformPackageVersion").Single().Value;

        releaseVersion.Should().Be("3.0.0");
        platformVersion.Should().Be("$(Version)");
        buildProps.Descendants("NgbPlatformApiCompatibilityBaselineVersion")
            .Single()
            .Value
            .Should()
            .Be("3.0.0");
        buildProps.Descendants("NgbPlatformAssemblyVersion")
            .Single()
            .Value
            .Should()
            .Be("3.0.0.0");

        var crmProjects = Directory.EnumerateDirectories(root, "NGB.CRM*", SearchOption.TopDirectoryOnly)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.csproj"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var platformPackageRefs = new List<(string Project, string Id, string? Version)>();

        foreach (var project in crmProjects)
        {
            var doc = XDocument.Load(project);
            platformPackageRefs.AddRange(doc.Descendants("PackageReference")
                .Select(x => new
                {
                    Id = x.Attribute("Include")?.Value,
                    Version = x.Attribute("Version")?.Value
                })
                .Where(x => x.Id?.StartsWith("NGB.Platform.", StringComparison.Ordinal) == true)
                .Select(x => (Path.GetFileName(project), x.Id!, x.Version)));
        }

        platformPackageRefs.Should().NotBeEmpty();
        platformPackageRefs.Should().OnlyContain(x => x.Version == "$(NgbPlatformPackageVersion)");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NGB.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }
}
