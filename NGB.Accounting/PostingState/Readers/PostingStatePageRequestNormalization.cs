using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Accounting.PostingState.Readers;

/// <summary>
/// Shared normalization/validation for PostingLog queries.
///
/// Notes:
/// - Readers are allowed to be lenient for JSON DateTime deserialization (Unspecified => treat as UTC).
/// - Runtime-level wrappers (reports) are strict and require UTC kinds when bounds are provided.
/// - <see cref="PostingStatePageRequest.FromUtc"/> / <see cref="PostingStatePageRequest.ToUtc"/> may be omitted
///   by passing default(DateTime); these are treated as "no bound".
/// </summary>
public static class PostingStatePageRequestNormalization
{
    public enum UtcBoundsPolicy
    {
        /// <summary>
        /// Bounds must be UTC (DateTimeKind.Utc) when provided.
        /// </summary>
        StrictUtc = 0,

        /// <summary>
        /// Bounds are normalized for convenience:
        /// - Local => converted to UTC
        /// - Unspecified => treated as UTC (SpecifyKind)
        /// </summary>
        LenientAssumeUtc = 1
    }

    public enum BoundsValidationMode
    {
        /// <summary>
        /// Validate ordering only when both bounds are explicitly provided.
        /// </summary>
        BothExplicit = 0,

        /// <summary>
        /// Validate ordering after applying query defaults:
        /// FromUtc=MinValue, ToUtc=UtcNow+1 day.
        /// </summary>
        QueryDefaultsApplied = 1
    }

    public readonly record struct PostingLogQueryBounds(
        DateTime NowUtc,
        DateTime FromUtc,
        DateTime ToUtc,
        TimeSpan StaleAfter);

    /// <summary>
    /// Applies safe defaults (StaleAfter) and normalizes/validates bounds.
    /// Returns bounds suitable for SQL query parameters.
    /// </summary>
    public static PostingLogQueryBounds NormalizeForQuery(
        this PostingStatePageRequest request,
        UtcBoundsPolicy policy,
        BoundsValidationMode validationMode = BoundsValidationMode.QueryDefaultsApplied)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        // Default aligns with PostingLog recovery TTL in repository.
        request.StaleAfter ??= TimeSpan.FromMinutes(10);

        var nowUtc = DateTime.UtcNow;
        var normalizedFrom = NormalizeBound(request.FromUtc, nameof(request.FromUtc), policy);
        var normalizedTo = NormalizeBound(request.ToUtc, nameof(request.ToUtc), policy);

        // For reader convenience we may mutate bounds to ensure Kind=Utc.
        if (policy == UtcBoundsPolicy.LenientAssumeUtc)
        {
            request.FromUtc = normalizedFrom;
            request.ToUtc = normalizedTo;
        }

        var fromUtcForQuery = normalizedFrom == default
            ? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)
            : normalizedFrom;

        var toUtcForQuery = normalizedTo == default
            ? nowUtc.AddDays(1)
            : normalizedTo;

        var shouldValidate = validationMode switch
        {
            BoundsValidationMode.BothExplicit => normalizedFrom != default && normalizedTo != default,
            _ => true
        };

        if (shouldValidate && toUtcForQuery < fromUtcForQuery)
            throw new NgbArgumentOutOfRangeException(nameof(request.ToUtc), null, "To must be on or after From.");

        return new PostingLogQueryBounds(
            NowUtc: nowUtc,
            FromUtc: fromUtcForQuery,
            ToUtc: toUtcForQuery,
            StaleAfter: request.StaleAfter.Value);
    }

    private static DateTime NormalizeBound(DateTime value, string name, UtcBoundsPolicy policy)
    {
        if (value == default) return default;

        return policy switch
        {
            UtcBoundsPolicy.StrictUtc => EnsureAndReturnUtc(value, name),
            UtcBoundsPolicy.LenientAssumeUtc => value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            },
            _ => throw new NgbArgumentOutOfRangeException(nameof(policy), policy, "Unknown bounds policy.")
        };
    }

    private static DateTime EnsureAndReturnUtc(DateTime value, string name)
    {
        value.EnsureUtc(name);
        return value;
    }
}
