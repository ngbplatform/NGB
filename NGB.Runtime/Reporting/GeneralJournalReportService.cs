using NGB.Accounting.Reports.GeneralJournal;
using NGB.Persistence.Readers.Reports;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Reporting;

/// <summary>
/// General Journal runtime service: validates request and forwards keyset-paged reads.
/// </summary>
public sealed class GeneralJournalReportService(IGeneralJournalReader reader) : IGeneralJournalReportReader
{
    public async Task<GeneralJournalPage> GetPageAsync(
        GeneralJournalPageRequest request,
        CancellationToken ct = default)
    {
        ValidateRequest(request);
        return await reader.GetPageAsync(request, ct);
    }

    private static void ValidateRequest(GeneralJournalPageRequest request)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        if (request.ToInclusive < request.FromInclusive)
            throw new NgbArgumentOutOfRangeException(nameof(request.ToInclusive), request.ToInclusive, "To must be on or after From.");

        request.FromInclusive.EnsureMonthStart(nameof(request.FromInclusive));
        request.ToInclusive.EnsureMonthStart(nameof(request.ToInclusive));

        if (request.PageSize <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(request.PageSize), request.PageSize, "Page size must be greater than 0.");
    }
}
