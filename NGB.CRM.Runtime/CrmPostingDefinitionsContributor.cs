using NGB.CRM.Runtime.Documents.Validation;
using NGB.CRM.Runtime.Posting;
using NGB.Definitions;

namespace NGB.CRM.Runtime;

public sealed class CrmPostingDefinitionsContributor : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        builder.ExtendDocument(
            CrmCodes.LeadIntake,
            d => d
                .AddPostValidator<LeadIntakePostValidator>()
                .ReferenceRegisterPostingHandler<LeadIntakeReferenceRegisterPostingHandler>());
        builder.ExtendDocument(
            CrmCodes.LeadQualification,
            d => d
                .AddPostValidator<LeadQualificationPostValidator>()
                .ReferenceRegisterPostingHandler<LeadQualificationReferenceRegisterPostingHandler>());
        builder.ExtendDocument(
            CrmCodes.LeadConversion,
            d => d
                .AddPostValidator<LeadConversionPostValidator>()
                .ReferenceRegisterPostingHandler<LeadConversionReferenceRegisterPostingHandler>());
        builder.ExtendDocument(
            CrmCodes.OpportunityUpdate,
            d => d
                .AddPostValidator<OpportunityUpdatePostValidator>()
                .ReferenceRegisterPostingHandler<OpportunityUpdateReferenceRegisterPostingHandler>());
        builder.ExtendDocument(
            CrmCodes.Quote,
            d => d
                .AddPostValidator<QuotePostValidator>()
                .ReferenceRegisterPostingHandler<QuoteReferenceRegisterPostingHandler>());
        builder.ExtendDocument(
            CrmCodes.ActivityLog,
            d => d
                .AddPostValidator<ActivityLogPostValidator>()
                .ReferenceRegisterPostingHandler<ActivityLogReferenceRegisterPostingHandler>());
    }
}
