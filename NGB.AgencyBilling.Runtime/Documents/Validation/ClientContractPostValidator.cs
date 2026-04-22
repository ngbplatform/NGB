using NGB.AgencyBilling.Documents;
using NGB.AgencyBilling.References;
using NGB.AgencyBilling.Runtime.Validation;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Documents.Validation;

public sealed class ClientContractPostValidator(
    IAgencyBillingDocumentReaders readers,
    IAgencyBillingReferenceReaders references)
    : IDocumentPostValidator
{
    public string TypeCode => AgencyBillingCodes.ClientContract;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        AgencyBillingDocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(ClientContractPostValidator));

        var head = await readers.ReadClientContractHeadAsync(documentForUpdate.Id, ct);
        var lines = await readers.ReadClientContractLinesAsync(documentForUpdate.Id, ct);

        var project = await AgencyBillingCatalogValidationGuards.EnsureProjectAsync(head.ProjectId, "project_id", references, ct);
        await AgencyBillingCatalogValidationGuards.EnsureClientAsync(head.ClientId, "client_id", references, ct);
        AgencyBillingCatalogValidationGuards.EnsureProjectBelongsToClient(project, head.ClientId, "project_id", "client_id");

        if (head.PaymentTermsId is { } paymentTermsId && paymentTermsId != Guid.Empty)
            await AgencyBillingCatalogValidationGuards.EnsurePaymentTermsAsync(paymentTermsId, "payment_terms_id", references, ct);

        if (!head.IsActive)
            throw new NgbArgumentInvalidException("is_active", "Client Contract must be active before posting.");

        if (head.EffectiveTo is { } effectiveTo && effectiveTo < head.EffectiveFrom)
            throw new NgbArgumentInvalidException("effective_to", "Effective To must be on or after Effective From.");

        if (lines.Count == 0)
            throw new NgbArgumentInvalidException("lines", "Client Contract must contain at least one line.");

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var prefix = $"lines[{i}]";

            if (line.ServiceItemId is { } serviceItemId && serviceItemId != Guid.Empty)
                await AgencyBillingCatalogValidationGuards.EnsureServiceItemAsync(serviceItemId, $"{prefix}.service_item_id", references, ct);

            if (line.TeamMemberId is { } teamMemberId && teamMemberId != Guid.Empty)
                await AgencyBillingCatalogValidationGuards.EnsureTeamMemberAsync(teamMemberId, $"{prefix}.team_member_id", references, ct);

            if (line.BillingRate <= 0m)
                throw new NgbArgumentInvalidException($"{prefix}.billing_rate", "Billing Rate must be greater than zero.");

            if (line.CostRate is not null && line.CostRate < 0m)
                throw new NgbArgumentInvalidException($"{prefix}.cost_rate", "Cost Rate must be zero or greater.");

            if (line.ActiveTo is { } activeTo && line.ActiveFrom is { } activeFrom && activeTo < activeFrom)
                throw new NgbArgumentInvalidException($"{prefix}.active_to", "Active To must be on or after Active From.");

            if (line.ServiceItemId is null && line.TeamMemberId is null && string.IsNullOrWhiteSpace(line.ServiceTitle))
            {
                throw new NgbArgumentInvalidException(
                    prefix,
                    "Contract line must specify at least a Service Item, Team Member, or Service Title.");
            }
        }
    }
}
