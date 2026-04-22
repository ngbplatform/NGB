using NGB.AgencyBilling.Runtime.Catalogs.Validation;
using NGB.Definitions;

namespace NGB.AgencyBilling.Runtime;

public sealed class AgencyBillingCatalogValidationDefinitionsContributor : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        builder.ExtendCatalog(AgencyBillingCodes.Project, c => c.AddValidator<ProjectCatalogUpsertValidator>());
        builder.ExtendCatalog(AgencyBillingCodes.RateCard, c => c.AddValidator<RateCardCatalogUpsertValidator>());
        builder.ExtendCatalog(AgencyBillingCodes.AccountingPolicy, c => c.AddValidator<AccountingPolicyCatalogUpsertValidator>());
    }
}
