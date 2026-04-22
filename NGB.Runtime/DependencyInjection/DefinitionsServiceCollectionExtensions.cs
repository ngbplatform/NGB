using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Definitions;

namespace NGB.Runtime.DependencyInjection;

/// <summary>
/// Registers the Definitions registry which aggregates module-contributed definitions.
/// This is intentionally reflection-free: modules must register <see cref="IDefinitionsContributor"/> explicitly.
/// </summary>
public static class DefinitionsServiceCollectionExtensions
{
    public static IServiceCollection AddNgbDefinitions(this IServiceCollection services)
    {
        services.TryAddSingleton(sp =>
        {
            var contributors = sp.GetServices<IDefinitionsContributor>();
            var builder = new DefinitionsBuilder();
            
            foreach (var c in contributors)
            {
                c.Contribute(builder);
            }

            return builder.Build();
        });

        return services;
    }
}
