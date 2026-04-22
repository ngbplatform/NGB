namespace NGB.AgencyBilling.Derivations;

public interface IAgencyBillingInvoiceDraftDerivationReader
{
    Task<AgencyBillingInvoiceDraftDefaults?> ResolveDefaultsAsync(
        Guid clientId,
        Guid projectId,
        DateOnly workDate,
        CancellationToken ct = default);

    Task<bool> HasExistingInvoiceForTimesheetAsync(
        Guid sourceTimesheetId,
        CancellationToken ct = default);
}
