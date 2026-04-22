using NGB.Definitions;
using NGB.AgencyBilling.Runtime.Documents.Validation;
using NGB.AgencyBilling.Runtime.Posting;

namespace NGB.AgencyBilling.Runtime;

public sealed class AgencyBillingPostingDefinitionsContributor : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        builder.ExtendDocument(
            AgencyBillingCodes.ClientContract,
            d => d
                .AddPostValidator<ClientContractPostValidator>()
                .ReferenceRegisterPostingHandler<ClientContractReferenceRegisterPostingHandler>());

        builder.ExtendDocument(
            AgencyBillingCodes.Timesheet,
            d => d
                .AddPostValidator<TimesheetPostValidator>()
                .OperationalRegisterPostingHandler<TimesheetOperationalRegisterPostingHandler>());

        builder.ExtendDocument(
            AgencyBillingCodes.SalesInvoice,
            d => d
                .AddPostValidator<SalesInvoicePostValidator>()
                .PostingHandler<SalesInvoicePostingHandler>()
                .OperationalRegisterPostingHandler<SalesInvoiceOperationalRegisterPostingHandler>());

        builder.ExtendDocument(
            AgencyBillingCodes.CustomerPayment,
            d => d
                .AddPostValidator<CustomerPaymentPostValidator>()
                .PostingHandler<CustomerPaymentPostingHandler>()
                .OperationalRegisterPostingHandler<CustomerPaymentOperationalRegisterPostingHandler>());
    }
}
