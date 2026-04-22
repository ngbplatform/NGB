using NGB.Tools.Exceptions;

namespace NGB.Definitions.Catalogs.Validation;

/// <summary>
/// Optional per-catalog-type validator invoked during Create/Update (head upsert).
/// This is the hook for enforcing module invariants that cannot be expressed by static
/// metadata (e.g., conditional required fields) and for producing friendly errors
/// instead of raw database constraint failures.
/// </summary>
public interface ICatalogUpsertValidator
{
    /// <summary>
    /// Catalog type code this validator is intended for.
    /// Used only for fail-fast diagnostics.
    /// </summary>
    string TypeCode { get; }

    /// <summary>
    /// Validates a catalog head upsert.
    /// <see cref="CatalogUpsertValidationContext.Fields"/> contains the effective values
    /// (existing values merged with payload updates for Update operations).
    /// </summary>
    /// <exception cref="NgbValidationException">Throw to reject the request with a friendly validation error.</exception>
    Task ValidateUpsertAsync(CatalogUpsertValidationContext context, CancellationToken ct);
}
