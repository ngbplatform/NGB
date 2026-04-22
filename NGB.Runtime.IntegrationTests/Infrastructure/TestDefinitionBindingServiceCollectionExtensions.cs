using Microsoft.Extensions.DependencyInjection;
using NGB.Definitions.Catalogs.Validation;
using NGB.Definitions.Documents.Approval;
using NGB.Definitions.Documents.Numbering;
using NGB.Definitions.Documents.Posting;
using NGB.Definitions.Documents.Validation;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.Documents.Storage;
using NGB.Runtime.Documents.Derivations;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

internal static class TestDefinitionBindingServiceCollectionExtensions
{
    private static readonly Type[] SupportedContracts =
    [
        typeof(IDocumentTypeStorage),
        typeof(ICatalogTypeStorage),
        typeof(IDocumentPostingHandler),
        typeof(IDocumentOperationalRegisterPostingHandler),
        typeof(IDocumentReferenceRegisterPostingHandler),
        typeof(IDocumentDraftValidator),
        typeof(IDocumentPostValidator),
        typeof(IDocumentNumberingPolicy),
        typeof(IDocumentApprovalPolicy),
        typeof(ICatalogUpsertValidator),
        typeof(IDocumentDerivationHandler)
    ];

    public static IServiceCollection AddTestDefinitionBindingAliases(this IServiceCollection services)
    {
        var concreteDescriptors = services
            .Where(static descriptor => descriptor.ServiceType is { IsAbstract: false, IsInterface: false })
            .ToArray();

        foreach (var descriptor in concreteDescriptors)
        {
            foreach (var contract in SupportedContracts)
            {
                if (!contract.IsAssignableFrom(descriptor.ServiceType))
                    continue;

                if (ContractAlreadyExposesImplementation(services, contract, descriptor.ServiceType))
                    continue;

                services.Add(ServiceDescriptor.Describe(
                    contract,
                    sp => sp.GetRequiredService(descriptor.ServiceType),
                    descriptor.Lifetime));
            }
        }

        return services;
    }

    private static bool ContractAlreadyExposesImplementation(
        IServiceCollection services,
        Type contract,
        Type implementationType)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType != contract)
                continue;

            if (descriptor.ImplementationType == implementationType)
                return true;

            if (descriptor.ImplementationInstance?.GetType() == implementationType)
                return true;
        }

        return false;
    }
}
