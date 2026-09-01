using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.BackgroundJobs.Catalog;
using NGB.BackgroundJobs.Contracts;

namespace NGB.BackgroundJobs.Tests.Catalog;

public sealed class PlatformJobCatalog_Contract_P0Tests
{
    [Fact]
    public void BackgroundJobsAssembly_DoesNotReferenceDatabaseClientOrMicroOrm()
    {
        var forbiddenReferences = new[] { "Dapper", "Dapper.AOT", "Npgsql" };
        var references = typeof(IPlatformBackgroundJob).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => forbiddenReferences.Contains(name, StringComparer.Ordinal))
            .ToArray();

        references.Should().BeEmpty(
            "provider clients and SQL execution belong in persistence adapter assemblies");
    }

    [Fact]
    public void PlatformJobs_DependOnlyOnProviderNeutralBoundaries()
    {
        var forbiddenNamespacePrefixes = new[]
        {
            "Dapper",
            "Npgsql",
            "NGB.PostgreSql"
        };

        var providerSpecificParameters = GetPlatformJobTypes()
            .SelectMany(type => type.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .Where(parameter => forbiddenNamespacePrefixes.Any(prefix =>
                parameter.ParameterType.Namespace?.StartsWith(prefix, StringComparison.Ordinal) == true))
            .Select(parameter => $"{parameter.Member.DeclaringType?.FullName}: {parameter.ParameterType.FullName}")
            .ToArray();

        providerSpecificParameters.Should().BeEmpty(
            "job orchestration must depend on provider-neutral contracts; SQL belongs in persistence adapters");
    }

    [Fact]
    public void PlatformJobCatalog_All_MustHaveExactlyOne_JobImplementation_WithMatchingJobId()
    {
        var expected = PlatformJobCatalog.All.ToHashSet(StringComparer.Ordinal);

        var jobTypes = GetPlatformJobTypes();

        jobTypes.Length.Should().BeGreaterThan(0);

        var byId = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var t in jobTypes)
        {
            var job = (IPlatformBackgroundJob)CreateInstanceWithMocks(t);
            job.JobId.Should().NotBeNullOrWhiteSpace();

            // Ensure no duplicates by JobId.
            byId.ContainsKey(job.JobId).Should().BeFalse(
                "duplicate JobId '{0}' found on types {1} and {2}",
                job.JobId,
                byId.GetValueOrDefault(job.JobId),
                t);

            byId[job.JobId] = t;
        }

        byId.Keys.Should().BeEquivalentTo(expected);
    }

    private static Type[] GetPlatformJobTypes() => typeof(IPlatformBackgroundJob).Assembly
        .GetTypes()
        .Where(t => t is { IsAbstract: false, IsInterface: false } &&
                    typeof(IPlatformBackgroundJob).IsAssignableFrom(t) &&
                    string.Equals(t.Namespace, "NGB.BackgroundJobs.Jobs", StringComparison.Ordinal))
        .ToArray();

    private static object CreateInstanceWithMocks(Type type)
    {
        var ctor = type.GetConstructors().Single();

        var services = new ServiceCollection();

        foreach (var p in ctor.GetParameters())
        {
            var pt = p.ParameterType;

            // ILogger<T>
            if (pt.IsGenericType && pt.GetGenericTypeDefinition() == typeof(Microsoft.Extensions.Logging.ILogger<>))
            {
                services.AddSingleton(pt, CreateMoqObject(pt));
                continue;
            }

            if (pt.IsInterface || pt.IsAbstract)
            {
                services.AddSingleton(pt, CreateMoqObject(pt));
                continue;
            }

            // As a last resort allow DI to construct it if possible.
            services.AddSingleton(pt);
        }

        var sp = services.BuildServiceProvider();

        return ActivatorUtilities.CreateInstance(sp, type);
    }

    private static object CreateMoqObject(Type serviceType)
    {
        var mockType = typeof(Mock<>).MakeGenericType(serviceType);
        var mock = Activator.CreateInstance(mockType)!;

        // Moq.Mock<T> has both "Object" on the generic type and a hidden member on the base type,
        // so Type.GetProperty("Object") can throw AmbiguousMatchException.
        var objectProp =
            mockType.GetProperty("Object", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly) ??
            mockType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Single(p => p.Name == "Object" && p.PropertyType == serviceType);

        return objectProp.GetValue(mock)!;
    }
}
