using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.Actions;

/// <summary>
/// The single DI boundary for document-action component types declared by metadata.
/// Keeping type activation here prevents the dispatcher and evaluator from becoming service locators.
/// </summary>
internal interface IDocumentActionComponentResolver
{
    IDocumentActionHandler ResolveHandler(Type componentType);

    IDocumentActionAuthorizationEvaluator ResolveAuthorizationEvaluator(Type componentType);

    IDocumentActionAvailabilityEvaluator ResolveAvailabilityEvaluator(Type componentType);
}

internal sealed class DocumentActionComponentResolver(IServiceProvider services) : IDocumentActionComponentResolver
{
    public IDocumentActionHandler ResolveHandler(Type componentType) => Resolve<IDocumentActionHandler>(componentType);

    public IDocumentActionAuthorizationEvaluator ResolveAuthorizationEvaluator(Type componentType)
    {
        EnsurePureEvaluator(componentType);
        return Resolve<IDocumentActionAuthorizationEvaluator>(componentType);
    }

    public IDocumentActionAvailabilityEvaluator ResolveAvailabilityEvaluator(Type componentType)
    {
        EnsurePureEvaluator(componentType);
        return Resolve<IDocumentActionAvailabilityEvaluator>(componentType);
    }

    private T Resolve<T>(Type componentType)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(componentType);

        var component = services.GetRequiredService(componentType);
        return component as T
            ?? throw new NgbConfigurationViolationException(
                $"Registered document-action component '{componentType.FullName}' does not implement '{typeof(T).FullName}'.");
    }

    internal static void EnsurePureEvaluator(Type componentType)
    {
        var dependencyConstructor = componentType
            .GetConstructors(System.Reflection.BindingFlags.Instance
                             | System.Reflection.BindingFlags.Public
                             | System.Reflection.BindingFlags.NonPublic)
            .FirstOrDefault(static constructor => constructor.GetParameters().Length > 0);

        if (dependencyConstructor is null)
            return;

        throw new NgbConfigurationViolationException(
            $"Document-action evaluator '{componentType.FullName}' must be pure and cannot declare constructor dependencies. " +
            $"Load I/O-bound facts through {nameof(IDocumentActionContextEnricher)} instead.");
    }
}
