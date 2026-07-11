namespace NGB.CRM.Documents;

public interface ICrmDocumentReaders
{
    Task<CrmLeadIntakeHead> ReadLeadIntakeHeadAsync(Guid documentId, CancellationToken ct = default);

    Task<CrmLeadQualificationHead> ReadLeadQualificationHeadAsync(Guid documentId, CancellationToken ct = default);

    Task<CrmLeadConversionHead> ReadLeadConversionHeadAsync(Guid documentId, CancellationToken ct = default);

    Task<CrmOpportunityUpdateHead> ReadOpportunityUpdateHeadAsync(Guid documentId, CancellationToken ct = default);

    Task<CrmQuoteHead> ReadQuoteHeadAsync(Guid documentId, CancellationToken ct = default);

    Task<IReadOnlyList<CrmQuoteLine>> ReadQuoteLinesAsync(Guid documentId, CancellationToken ct = default);

    Task<CrmActivityLogHead> ReadActivityLogHeadAsync(Guid documentId, CancellationToken ct = default);
}
