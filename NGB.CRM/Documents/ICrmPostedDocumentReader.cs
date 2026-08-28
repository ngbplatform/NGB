namespace NGB.CRM.Documents;

public interface ICrmPostedDocumentReader
{
    Task<IReadOnlyList<Guid>> GetIdsPageAfterAsync(
        string documentType,
        Guid? afterId,
        int limit,
        CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetIdsMissingReferenceRegisterPostPageAfterAsync(
        string documentType,
        Guid primaryRegisterId,
        Guid? createOpportunityRegisterId,
        Guid? afterId,
        int limit,
        CancellationToken ct = default);
}
