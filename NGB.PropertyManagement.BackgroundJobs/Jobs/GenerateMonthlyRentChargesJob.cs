using System.Globalization;
using Hangfire;
using NGB.BackgroundJobs.Contracts;
using NGB.PropertyManagement.BackgroundJobs.Catalog;
using NGB.PropertyManagement.BackgroundJobs.Services;

namespace NGB.PropertyManagement.BackgroundJobs.Jobs;

internal sealed class GenerateMonthlyRentChargesJob(
    GenerateMonthlyRentChargesService service,
    TimeProvider timeProvider,
    IBackgroundJobClient? backgroundJobs = null)
    : IPlatformBackgroundJob
{
    public string JobId => PropertyManagementBackgroundJobCatalog.GenerateMonthlyRentCharges;

    public Task RunAsync(CancellationToken ct)
        => RunChunkAndContinueAsync(
            DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime),
            cursor: null,
            ct);

    public Task ContinueAsync(
        string asOfUtc,
        string? afterStartOnUtc,
        Guid? afterLeaseId,
        string? afterCandidateDueOnUtc,
        Guid? afterCandidateLeaseId,
        string? afterCandidatePeriodFromUtc,
        CancellationToken ct)
        => RunChunkAndContinueAsync(
            ParseDate(asOfUtc),
            new RentChargeGenerationCursor(
                ParseOptionalDate(afterStartOnUtc),
                afterLeaseId,
                ParseOptionalDate(afterCandidateDueOnUtc),
                afterCandidateLeaseId,
                ParseOptionalDate(afterCandidatePeriodFromUtc)),
            ct);

    private async Task RunChunkAndContinueAsync(
        DateOnly asOfUtc,
        RentChargeGenerationCursor? cursor,
        CancellationToken ct)
    {
        var result = await service.ExecuteChunkAsync(asOfUtc, cursor, ct);
        if (result.Continuation is not { } next || backgroundJobs is null)
            return;

        backgroundJobs.Enqueue<GenerateMonthlyRentChargesJob>(job => job.ContinueAsync(
            FormatDate(asOfUtc),
            FormatDate(next.AfterStartOnUtc),
            next.AfterLeaseId,
            FormatDate(next.AfterCandidateDueOnUtc),
            next.AfterCandidateLeaseId,
            FormatDate(next.AfterCandidatePeriodFromUtc),
            CancellationToken.None));
    }

    private static string FormatDate(DateOnly value)
        => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? FormatDate(DateOnly? value)
        => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ParseDate(string value)
        => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly? ParseOptionalDate(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : ParseDate(value);
}
