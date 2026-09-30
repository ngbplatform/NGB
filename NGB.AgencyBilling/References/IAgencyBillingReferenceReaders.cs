namespace NGB.AgencyBilling.References;

public interface IAgencyBillingReferenceReaders
{
    Task<AgencyBillingClientReference?> ReadClientAsync(Guid clientId, CancellationToken ct = default);

    Task<AgencyBillingProjectReference?> ReadProjectAsync(Guid projectId, CancellationToken ct = default);

    Task<AgencyBillingTeamMemberReference?> ReadTeamMemberAsync(Guid teamMemberId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, AgencyBillingTeamMemberReference>> ReadTeamMembersAsync(
        IReadOnlyCollection<Guid> teamMemberIds,
        CancellationToken ct = default);

    Task<AgencyBillingServiceItemReference?> ReadServiceItemAsync(Guid serviceItemId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, AgencyBillingServiceItemReference>> ReadServiceItemsAsync(
        IReadOnlyCollection<Guid> serviceItemIds,
        CancellationToken ct = default);

    Task<AgencyBillingPaymentTermsReference?> ReadPaymentTermsAsync(Guid paymentTermsId, CancellationToken ct = default);
}
