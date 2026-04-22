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
    public void PlatformJobCatalog_All_MustHaveExactlyOne_JobImplementation_WithMatchingJobId()
    {
        var expected = PlatformJobCatalog.All.ToHashSet(StringComparer.Ordinal);

        var jobTypes = typeof(IPlatformBackgroundJob).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } &&
                        typeof(IPlatformBackgroundJob).IsAssignableFrom(t) &&
                        string.Equals(t.Namespace, "NGB.BackgroundJobs.Jobs", StringComparison.Ordinal))
            .ToArray();

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
