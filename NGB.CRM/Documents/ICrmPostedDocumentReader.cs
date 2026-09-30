namespace NGB.CRM.Documents;

public interface ICrmPostedDocumentReader
{
    Task<IReadOnlyList<Guid>> GetIdsMissingReferenceRegisterPostPageAfterAsync(
        string documentType,
        Guid primaryRegisterId,
        Guid? createOpportunityRegisterId,
        Guid? afterId,
        int limit,
        CancellationToken ct = default);
}
