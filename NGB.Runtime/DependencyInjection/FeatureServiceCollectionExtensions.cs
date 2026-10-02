using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.FeatureManagement;
using NGB.Application.Abstractions.Features;
using NGB.Core.Features;
using NGB.Runtime.Features;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.DependencyInjection;

public static class FeatureServiceCollectionExtensions
{
    /// <summary>Registers deployment-wide flags. Configuration changes require an application restart.</summary>
    public static IServiceCollection AddNgbFeatureManagement(
        this IServiceCollection services,
        IConfiguration configuration,
        IEnumerable<NgbFeatureDefinition>? additionalFeatures = null)
    {
        var registered = services.FirstOrDefault(x => x.ServiceType == typeof(NgbFeatureRegistry))?.ImplementationInstance as NgbFeatureRegistry;
        var registry = registered ?? new NgbFeatureRegistry(
        [
            new(NgbFeatures.Attachments, "Attachments", "Attachments & Notes"),
            new(NgbFeatures.Notes, "Notes", "Attachments & Notes"),
            .. additionalFeatures ?? []
        ]);
        var flags = registry.Definitions.ToDictionary(x => x.Code, _ => (string?)bool.FalseString);

        foreach (var section in configuration.GetSection("FeatureManagement").GetChildren())
        {
            if (!registry.Contains(section.Key) || section.GetChildren().Any() || !bool.TryParse(section.Value, out var enabled))
                throw new NgbConfigurationViolationException($"FeatureManagement:{section.Key} must name a registered feature and contain true or false.");

            flags[section.Key] = enabled.ToString();
        }

        if (registered is not null)
        {
            var previous = (FeatureRegistration)services.Single(x => x.ServiceType == typeof(FeatureRegistration)).ImplementationInstance!;
            if (additionalFeatures is not null || flags.Any(x => previous.Flags[x.Key] != x.Value))
                throw new NgbConfigurationViolationException("Register feature management once, before registering feature modules, using the same configuration.");

            return services;
        }

        // Freeze the configuration so feature checks agree with startup service composition.
        var snapshot = new ConfigurationBuilder().AddInMemoryCollection(flags).Build();
        services.AddFeatureManagement(snapshot);
        services.AddSingleton(registry);
        services.AddSingleton(new FeatureRegistration(flags));
        services.TryAddScoped<INgbFeatureService, NgbFeatureService>();

        return services;
    }

    private sealed record FeatureRegistration(IReadOnlyDictionary<string, string?> Flags);
}
