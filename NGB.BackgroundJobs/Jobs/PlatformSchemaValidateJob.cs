using Microsoft.Extensions.Logging;
using NGB.BackgroundJobs.Contracts;
using NGB.Persistence.Schema;
using NGB.Runtime.Catalogs;
using NGB.Runtime.Documents;
using NGB.Tools.Extensions;

namespace NGB.BackgroundJobs.Jobs;

/// <summary>
/// Nightly: validates core + hybrid metadata schema contracts.
///
/// This job is intentionally provider-agnostic: it relies only on the platform validation abstractions.
/// Any mismatch should fail the job to surface drift early.
/// </summary>
public sealed class PlatformSchemaValidateJob(
    IDocumentsCoreSchemaValidationService documentsCore,
    IAccountingCoreSchemaValidationService accountingCore,
    IOperationalRegistersCoreSchemaValidationService operationalRegistersCore,
    IReferenceRegistersCoreSchemaValidationService referenceRegistersCore,
    ICatalogSchemaValidationService catalogs,
    IDocumentSchemaValidationService documents,
    ILogger<PlatformSchemaValidateJob> logger,
    IJobRunMetrics metrics,
    TimeProvider? timeProvider = null)
    : IPlatformBackgroundJob
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public string JobId => "platform.schema.validate";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var startedAt = _timeProvider.GetUtcNowDateTime();
        logger.LogInformation("[{JobId}] START at {StartedAtUtc:O}", JobId, startedAt);

        const int totalValidations = 6;
        metrics.Set("schemas_total", totalValidations);
        metrics.Set("schemas_validated", 0);
        metrics.Set("validations_total", totalValidations);
        metrics.Set("validations", 0);

        var validated = 0;

        await documentsCore.ValidateAsync(cancellationToken);
        validated++;

        metrics.Set("schemas_validated", validated);
        metrics.Set("validations", validated);

        await accountingCore.ValidateAsync(cancellationToken);
        validated++;

        metrics.Set("schemas_validated", validated);
        metrics.Set("validations", validated);

        await operationalRegistersCore.ValidateAsync(cancellationToken);
        validated++;

        metrics.Set("schemas_validated", validated);
        metrics.Set("validations", validated);

        await referenceRegistersCore.ValidateAsync(cancellationToken);
        validated++;

        metrics.Set("schemas_validated", validated);
        metrics.Set("validations", validated);

        await catalogs.ValidateAllAsync(cancellationToken);
        validated++;

        metrics.Set("schemas_validated", validated);
        metrics.Set("validations", validated);

        await documents.ValidateAllAsync(cancellationToken);
        validated++;

        metrics.Set("schemas_validated", validated);
        metrics.Set("validations", validated);

        var finishedAt = _timeProvider.GetUtcNowDateTime();
        logger.LogInformation(
            "[{JobId}] OK. Validations={Validated}. DurationMs={DurationMs}",
            JobId,
            validated,
            (long)(finishedAt - startedAt).TotalMilliseconds);
    }
}
