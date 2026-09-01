using FluentAssertions;
using NGB.PostgreSql.DependencyInjection;
using Xunit;

namespace NGB.PostgreSql.Tests.Architecture;

public sealed class PostgresProviderDependencyBoundaryTests
{
    [Fact]
    public void PostgreSqlAssembly_DoesNotReferenceBackgroundJobInfrastructure()
    {
        var forbiddenReferences = new[]
        {
            "Hangfire.Core",
            "Hangfire.PostgreSql",
            "Newtonsoft.Json"
        };

        var references = typeof(PostgresServiceCollectionExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => forbiddenReferences.Contains(name, StringComparer.Ordinal))
            .ToArray();

        references.Should().BeEmpty("scheduler storage and its serializer belong in NGB.BackgroundJobs.PostgreSql");
    }
}
