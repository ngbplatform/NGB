using NGB.Definitions;
using NGB.Definitions.Catalogs.Validation;
using NGB.Tools.Exceptions;
using CertificationApp.Definitions;

namespace CertificationApp.Runtime;

public sealed class CheckpointRuntimeDefinitions : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        builder.ExtendCatalog(CheckpointDefinitions.CatalogCode, catalog => catalog.AddValidator<CheckpointValidator>());
    }
}

public sealed class CheckpointValidator : ICatalogUpsertValidator
{
    public string TypeCode => CheckpointDefinitions.CatalogCode;

    public Task ValidateUpsertAsync(CatalogUpsertValidationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(context.Fields.GetValueOrDefault("display")?.ToString()))
            throw new InvalidCheckpointException();

        return Task.CompletedTask;
    }
}

public sealed class InvalidCheckpointException()
    : NgbValidationException("Checkpoint display is required.", "certification.checkpoint.display_required");
