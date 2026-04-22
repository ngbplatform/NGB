using NGB.Tools.Exceptions;
using System.Runtime.CompilerServices;

namespace NGB.Runtime.Internal;

internal static class DefinitionRuntimeBindingHelpers
{
    public static IReadOnlyList<TService> ToReadOnlyList<TService>(IEnumerable<TService> services)
        where TService : class
    {
        if (services is null)
            throw new NgbArgumentRequiredException(nameof(services));

        return services
            .Distinct(ReferenceComparer<TService>.Instance)
            .ToArray();
    }

    public static TService[] FindMatches<TService>(Type bindingType, IReadOnlyList<TService> services)
        where TService : class
    {
        if (bindingType is null)
            throw new NgbArgumentRequiredException(nameof(bindingType));

        if (services is null)
            throw new NgbArgumentRequiredException(nameof(services));

        return services
            .Where(bindingType.IsInstanceOfType)
            .Distinct(ReferenceComparer<TService>.Instance)
            .ToArray();
    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static ReferenceComparer<T> Instance { get; } = new();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
