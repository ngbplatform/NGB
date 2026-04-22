using NGB.Definitions;
using NGB.PropertyManagement.Runtime.Catalogs.Validation;

namespace NGB.PropertyManagement.Runtime;

/// <summary>
/// Binds runtime catalog validators to PM definitions.
/// This keeps NGB.PropertyManagement.Definitions free of Runtime dependencies.
/// </summary>
public sealed class PropertyManagementCatalogValidationDefinitionsContributor : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        builder.ExtendCatalog(PropertyManagementCodes.Party, c =>
        {
            c.AddValidator<PartyCatalogUpsertValidator>();
        });

        builder.ExtendCatalog(PropertyManagementCodes.Property, c =>
        {
            c.AddValidator<PropertyCatalogUpsertValidator>();
        });

        builder.ExtendCatalog(PropertyManagementCodes.BankAccount, c =>
        {
            c.AddValidator<BankAccountCatalogUpsertValidator>();
        });
    }
}
