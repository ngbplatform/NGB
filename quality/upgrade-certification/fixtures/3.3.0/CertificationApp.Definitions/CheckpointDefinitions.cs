using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;

namespace CertificationApp.Definitions;

public sealed class CheckpointDefinitions : IDefinitionsContributor
{
    public const string CatalogCode = "certification.checkpoint";

    public void Contribute(DefinitionsBuilder builder)
    {
        builder.AddCatalog(CatalogCode, catalog => catalog.Metadata(new CatalogTypeMetadata(
            CatalogCode: CatalogCode,
            DisplayName: "Checkpoint",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_certification_checkpoint",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true)
                    ],
                    Indexes: [])
            ],
            Presentation: new CatalogPresentationMetadata("cat_certification_checkpoint", "display"),
            Version: new CatalogMetadataVersion(1, "certification"))));
    }
}
