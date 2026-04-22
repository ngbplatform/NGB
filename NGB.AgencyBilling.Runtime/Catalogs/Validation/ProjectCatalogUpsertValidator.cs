using NGB.AgencyBilling.Runtime.Validation;
using NGB.AgencyBilling.References;
using NGB.Definitions.Catalogs.Validation;
using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Catalogs.Validation;

public sealed class ProjectCatalogUpsertValidator(IAgencyBillingReferenceReaders references) : ICatalogUpsertValidator
{
    public string TypeCode => AgencyBillingCodes.Project;

    public async Task ValidateUpsertAsync(CatalogUpsertValidationContext context, CancellationToken ct)
    {
        if (!string.Equals(context.TypeCode, TypeCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new NgbConfigurationViolationException(
                $"{nameof(ProjectCatalogUpsertValidator)} is configured for '{TypeCode}', not '{context.TypeCode}'.");
        }

        var clientId = AgencyBillingValidationValueReaders.ReadGuid(context.Fields, "client_id");
        if (clientId is null || clientId == Guid.Empty)
            throw new NgbArgumentInvalidException("client_id", "Client is required.");

        await AgencyBillingCatalogValidationGuards.EnsureClientAsync(clientId.Value, "client_id", references, ct);

        var projectManagerId = AgencyBillingValidationValueReaders.ReadGuid(context.Fields, "project_manager_id");
        if (projectManagerId is { } resolvedProjectManagerId && resolvedProjectManagerId != Guid.Empty)
            await AgencyBillingCatalogValidationGuards.EnsureTeamMemberAsync(resolvedProjectManagerId, "project_manager_id", references, ct);

        var startDate = AgencyBillingValidationValueReaders.ReadDate(context.Fields, "start_date");
        var endDate = AgencyBillingValidationValueReaders.ReadDate(context.Fields, "end_date");
        if (startDate is not null && endDate is not null && endDate < startDate)
            throw new NgbArgumentInvalidException("end_date", "End Date must be on or after Start Date.");
    }
}
