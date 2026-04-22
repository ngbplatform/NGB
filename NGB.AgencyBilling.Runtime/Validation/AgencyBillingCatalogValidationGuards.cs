using NGB.AgencyBilling.Enums;
using NGB.AgencyBilling.References;
using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Validation;

internal static class AgencyBillingCatalogValidationGuards
{
    public static async Task<AgencyBillingClientReference> EnsureClientAsync(
        Guid clientId,
        string fieldPath,
        IAgencyBillingReferenceReaders references,
        CancellationToken ct,
        bool requireOperationallyActive = false)
    {
        var client = await GetRequiredClientAsync(clientId, fieldPath, references, ct);
        var status = client.Status;

        if (status is AgencyBillingClientStatus.Inactive)
            throw new NgbArgumentInvalidException(fieldPath, "Selected client is inactive.");

        if (requireOperationallyActive && status is not AgencyBillingClientStatus.Active)
            throw new NgbArgumentInvalidException(fieldPath, "Selected client must be Active.");

        return client;
    }

    public static Task<AgencyBillingTeamMemberReference> EnsureTeamMemberAsync(
        Guid teamMemberId,
        string fieldPath,
        IAgencyBillingReferenceReaders references,
        CancellationToken ct)
        => GetRequiredActiveReferenceAsync(
            teamMemberId,
            fieldPath,
            "team member",
            references.ReadTeamMemberAsync,
            ct);

    public static async Task<AgencyBillingProjectReference> EnsureProjectAsync(
        Guid projectId,
        string fieldPath,
        IAgencyBillingReferenceReaders references,
        CancellationToken ct,
        bool requireOperationallyActive = false)
    {
        var project = await GetRequiredProjectAsync(projectId, fieldPath, references, ct);
        var status = project.Status;

        if (requireOperationallyActive && status is not AgencyBillingProjectStatus.Active)
            throw new NgbArgumentInvalidException(fieldPath, "Selected project must be Active.");

        return project;
    }

    public static Task<AgencyBillingServiceItemReference> EnsureServiceItemAsync(
        Guid serviceItemId,
        string fieldPath,
        IAgencyBillingReferenceReaders references,
        CancellationToken ct)
        => GetRequiredActiveReferenceAsync(
            serviceItemId,
            fieldPath,
            "service item",
            references.ReadServiceItemAsync,
            ct);

    public static Task<AgencyBillingPaymentTermsReference> EnsurePaymentTermsAsync(
        Guid paymentTermsId,
        string fieldPath,
        IAgencyBillingReferenceReaders references,
        CancellationToken ct)
        => GetRequiredActiveReferenceAsync(
            paymentTermsId,
            fieldPath,
            "payment terms",
            references.ReadPaymentTermsAsync,
            ct);

    public static void EnsureProjectBelongsToClient(
        AgencyBillingProjectReference project,
        Guid expectedClientId,
        string projectFieldPath,
        string clientFieldPath)
    {
        if (project.ClientId is null || project.ClientId == Guid.Empty)
        {
            throw new NgbConfigurationViolationException(
                $"Project '{project.Id}' does not have a valid client_id in reference data.");
        }

        if (project.ClientId.Value != expectedClientId)
        {
            throw new NgbArgumentInvalidException(
                projectFieldPath,
                $"Selected project does not belong to the client specified in '{clientFieldPath}'.");
        }
    }

    private static async Task<AgencyBillingClientReference> GetRequiredClientAsync(
        Guid id,
        string fieldPath,
        IAgencyBillingReferenceReaders references,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
            throw new NgbArgumentInvalidException(fieldPath, $"{fieldPath} is required.");

        var client = await references.ReadClientAsync(id, ct);
        if (client is null)
            throw new NgbArgumentInvalidException(fieldPath, "Referenced client was not found.");

        if (client.IsMarkedForDeletion)
            throw new NgbArgumentInvalidException(fieldPath, "Referenced client is not available.");

        if (!client.IsActive)
            throw new NgbArgumentInvalidException(fieldPath, "Referenced client is inactive.");

        return client;
    }

    private static async Task<AgencyBillingProjectReference> GetRequiredProjectAsync(
        Guid id,
        string fieldPath,
        IAgencyBillingReferenceReaders references,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
            throw new NgbArgumentInvalidException(fieldPath, $"{fieldPath} is required.");

        var project = await references.ReadProjectAsync(id, ct);
        if (project is null)
            throw new NgbArgumentInvalidException(fieldPath, "Referenced project was not found.");

        if (project.IsMarkedForDeletion)
            throw new NgbArgumentInvalidException(fieldPath, "Referenced project is not available.");

        if (!project.IsActive)
            throw new NgbArgumentInvalidException(fieldPath, "Referenced project is inactive.");

        return project;
    }

    private static async Task<TReference> GetRequiredActiveReferenceAsync<TReference>(
        Guid id,
        string fieldPath,
        string description,
        Func<Guid, CancellationToken, Task<TReference?>> readAsync,
        CancellationToken ct)
        where TReference : class
    {
        if (id == Guid.Empty)
            throw new NgbArgumentInvalidException(fieldPath, $"{fieldPath} is required.");

        var item = await readAsync(id, ct);
        if (item is null)
            throw new NgbArgumentInvalidException(fieldPath, $"Referenced {description} was not found.");

        switch (item)
        {
            case AgencyBillingTeamMemberReference { IsMarkedForDeletion: true }:
                throw new NgbArgumentInvalidException(fieldPath, $"Referenced {description} is not available.");
            case AgencyBillingServiceItemReference { IsMarkedForDeletion: true }:
                throw new NgbArgumentInvalidException(fieldPath, $"Referenced {description} is not available.");
            case AgencyBillingPaymentTermsReference { IsMarkedForDeletion: true }:
                throw new NgbArgumentInvalidException(fieldPath, $"Referenced {description} is not available.");
        }

        var isActive = item switch
        {
            AgencyBillingTeamMemberReference teamMember => teamMember.IsActive,
            AgencyBillingServiceItemReference serviceItem => serviceItem.IsActive,
            AgencyBillingPaymentTermsReference paymentTerms => paymentTerms.IsActive,
            _ => throw new NgbConfigurationViolationException($"Unsupported reference type '{typeof(TReference).Name}'.")
        };

        if (!isActive)
            throw new NgbArgumentInvalidException(fieldPath, $"Referenced {description} is inactive.");

        return item;
    }
}
