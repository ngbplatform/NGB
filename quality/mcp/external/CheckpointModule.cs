using Microsoft.Extensions.DependencyInjection;
using NGB.Definitions;
using NGB.Definitions.Catalogs.Validation;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Catalogs;
using NGB.Tools.Exceptions;

namespace ExternalMcp.Tests;

public sealed class CheckpointDefinitions : IDefinitionsContributor
{
    public const string Code = "mcp.checkpoint";

    public void Contribute(DefinitionsBuilder builder)
    {
        builder.AddCatalog(Code, catalog => catalog.Metadata(new CatalogTypeMetadata(
            Code, "Checkpoint",
            [
                new CatalogTableMetadata("cat_mcp_checkpoint", TableKind.Head,
                    [new("catalog_id", ColumnType.Guid, Required: true), new("display", ColumnType.String, Required: true)], [])
            ],
            new CatalogPresentationMetadata("cat_mcp_checkpoint", "display"), new CatalogMetadataVersion(1, "mcp-tests")))
            .AddValidator<CheckpointValidator>());
    }
}

public sealed class CheckpointValidator : ICatalogUpsertValidator
{
    public string TypeCode => CheckpointDefinitions.Code;

    public Task ValidateUpsertAsync(CatalogUpsertValidationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(context.Fields.GetValueOrDefault("display")?.ToString()))
            throw new NgbArgumentInvalidException("display", "Checkpoint display is required.");
        return Task.CompletedTask;
    }
}

public static class CheckpointRegistration
{
    public static IServiceCollection AddCheckpoint(this IServiceCollection services)
    {
        services.AddSingleton<IDefinitionsContributor, CheckpointDefinitions>();
        services.AddScoped<ICatalogUpsertValidator, CheckpointValidator>();
        services.AddScoped<ICatalogTypeStorage>(provider => new PostgresHeadCatalogTypeStorage(
            provider.GetRequiredService<IUnitOfWork>(), CheckpointDefinitions.Code, "cat_mcp_checkpoint",
            [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));
        return services;
    }
}
