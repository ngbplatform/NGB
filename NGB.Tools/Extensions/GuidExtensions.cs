using NGB.Tools.Exceptions;

namespace NGB.Tools.Extensions;

public static class GuidExtensions
{
    /// <summary>
    /// Ensures a required Guid argument is present and not <see cref="Guid.Empty"/>.
    /// 
    /// Use when callers pass <see cref="Guid.Empty"/> as a "missing" sentinel (e.g., API model binding).
    /// </summary>
    public static void EnsureRequired(this Guid value, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new NgbArgumentRequiredException(nameof(name));

        if (value == Guid.Empty)
            throw new NgbArgumentRequiredException(name);
    }

    /// <summary>
    /// Ensures the Guid argument is not <see cref="Guid.Empty"/>.
    /// </summary>
    public static void EnsureNonEmpty(this Guid value, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new NgbArgumentRequiredException(nameof(name));

        if (value == Guid.Empty)
            throw new NgbArgumentOutOfRangeException(name, value, $"{name} must be non-empty.");
    }
}
