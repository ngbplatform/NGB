using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NGB.Definitions;
using NGB.Runtime.DependencyInjection;
using NGB.Accounting.Posting.Validators;
using NGB.PostgreSql.DependencyInjection;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

internal static class IntegrationHostFactory
{
    public static IHost Create(string connectionString)
    {
        return Create(connectionString, configureTestServices: null);
    }

    public static IHost Create(
        string connectionString,
        Action<IServiceCollection>? configureTestServices)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                // Arrange
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Warning);
            })
            .ConfigureServices(services =>
            {
                // Act
                services.AddNgbRuntime();
                services.AddNgbPostgres(connectionString);

                // NGB.Runtime composes multiple submodules (catalogs/documents/etc.).
                // For accounting-focused integration tests we don't need full metadata + storage resolvers,
                // but we DO need the accounting validator required by PostingEngine.
                services.AddScoped<IAccountingPostingValidator, BasicAccountingPostingValidator>();

                configureTestServices?.Invoke(services);
                services.AddTestDefinitionBindingAliases();

                // Default IT definitions:
                // Many integration tests create draft documents/catalogs and only care about lifecycle/posting semantics,
                // so we register minimal type metadata by default. Individual tests may override or extend this
                // by providing their own INgbDefinitionsContributor registrations.
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IDefinitionsContributor, TestDocumentContributor>());
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IDefinitionsContributor, TestCatalogContributor>());
            })
            .UseDefaultServiceProvider(options =>
            {
                // Assert (DI correctness)
                //
                // NOTE:
                // ValidateOnBuild validates *all* registered services, including modules not used in these tests
                // (catalogs/documents metadata registries and storage resolvers). For integration tests we want to
                // validate scopes but avoid failing the container for unrelated modules.
                options.ValidateOnBuild = false;
                options.ValidateScopes = true;
            })
            .Build();
    }
}
