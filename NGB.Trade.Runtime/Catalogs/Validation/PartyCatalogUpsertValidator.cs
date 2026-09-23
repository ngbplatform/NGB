using NGB.Definitions.Catalogs.Validation;
using NGB.Tools.Exceptions;
using NGB.Trade.Runtime.Exceptions;

namespace NGB.Trade.Runtime.Catalogs.Validation;

public sealed class PartyCatalogUpsertValidator : ICatalogUpsertValidator
{
    public string TypeCode => TradeCodes.Party;

    public Task ValidateUpsertAsync(CatalogUpsertValidationContext context, CancellationToken ct)
    {
        if (!string.Equals(context.TypeCode, TypeCode, StringComparison.OrdinalIgnoreCase))
            throw new NgbConfigurationViolationException($"{nameof(PartyCatalogUpsertValidator)} is configured for '{TypeCode}', not '{context.TypeCode}'.");

        // CatalogService supplies parsed booleans and merges existing fields for partial updates.
        var isCustomer = context.Fields.TryGetValue("is_customer", out var customer) && customer is true;
        var isVendor = context.Fields.TryGetValue("is_vendor", out var vendor) && vendor is true;
        if (!isCustomer && !isVendor)
            throw new PartyRoleRequiredException(context.CatalogId);

        return Task.CompletedTask;
    }
}
