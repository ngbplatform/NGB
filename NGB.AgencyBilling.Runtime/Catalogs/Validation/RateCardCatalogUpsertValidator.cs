using NGB.AgencyBilling.Runtime.Validation;
using NGB.AgencyBilling.References;
using NGB.Definitions.Catalogs.Validation;
using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Catalogs.Validation;

public sealed class RateCardCatalogUpsertValidator(IAgencyBillingReferenceReaders references) : ICatalogUpsertValidator
{
    public string TypeCode => AgencyBillingCodes.RateCard;

    public async Task ValidateUpsertAsync(CatalogUpsertValidationContext context, CancellationToken ct)
    {
        if (!string.Equals(context.TypeCode, TypeCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new NgbConfigurationViolationException(
                $"{nameof(RateCardCatalogUpsertValidator)} is configured for '{TypeCode}', not '{context.TypeCode}'.");
        }

        var billingRate = AgencyBillingValidationValueReaders.ReadDecimal(context.Fields, "billing_rate");
        if (billingRate is null || billingRate <= 0m)
            throw new NgbArgumentInvalidException("billing_rate", "Billing Rate must be greater than zero.");

        var costRate = AgencyBillingValidationValueReaders.ReadDecimal(context.Fields, "cost_rate");
        if (costRate is not null && costRate < 0m)
            throw new NgbArgumentInvalidException("cost_rate", "Cost Rate must be zero or greater.");

        var effectiveFrom = AgencyBillingValidationValueReaders.ReadDate(context.Fields, "effective_from");
        var effectiveTo = AgencyBillingValidationValueReaders.ReadDate(context.Fields, "effective_to");
        if (effectiveFrom is not null && effectiveTo is not null && effectiveTo < effectiveFrom)
            throw new NgbArgumentInvalidException("effective_to", "Effective To must be on or after Effective From.");

        var clientId = AgencyBillingValidationValueReaders.ReadGuid(context.Fields, "client_id");
        var projectId = AgencyBillingValidationValueReaders.ReadGuid(context.Fields, "project_id");
        var teamMemberId = AgencyBillingValidationValueReaders.ReadGuid(context.Fields, "team_member_id");
        var serviceItemId = AgencyBillingValidationValueReaders.ReadGuid(context.Fields, "service_item_id");

        if (clientId is { } resolvedClientId && resolvedClientId != Guid.Empty)
            await AgencyBillingCatalogValidationGuards.EnsureClientAsync(resolvedClientId, "client_id", references, ct);

        if (projectId is { } resolvedProjectId && resolvedProjectId != Guid.Empty)
        {
            var project = await AgencyBillingCatalogValidationGuards.EnsureProjectAsync(resolvedProjectId, "project_id", references, ct);
            if (clientId is { } resolvedScopedClientId && resolvedScopedClientId != Guid.Empty)
                AgencyBillingCatalogValidationGuards.EnsureProjectBelongsToClient(project, resolvedScopedClientId, "project_id", "client_id");
        }

        if (teamMemberId is { } resolvedTeamMemberId && resolvedTeamMemberId != Guid.Empty)
            await AgencyBillingCatalogValidationGuards.EnsureTeamMemberAsync(resolvedTeamMemberId, "team_member_id", references, ct);

        if (serviceItemId is { } resolvedServiceItemId && resolvedServiceItemId != Guid.Empty)
            await AgencyBillingCatalogValidationGuards.EnsureServiceItemAsync(resolvedServiceItemId, "service_item_id", references, ct);
    }
}
