using NGB.Definitions;

namespace NGB.AgencyBilling.Runtime.Derivations;

public sealed class AgencyBillingDerivationDefinitionsContributor : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        builder.AddDocumentDerivation(
            AgencyBillingCodes.GenerateInvoiceDraftDerivation,
            configure: d => d
                .Name("Generate Invoice Draft")
                .From(AgencyBillingCodes.Timesheet)
                .To(AgencyBillingCodes.SalesInvoice)
                .Relationship("created_from")
                .Handler<GenerateInvoiceDraftFromTimesheetDerivationHandler>());
    }
}
