using FluentAssertions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Hybrid;
using NGB.Metadata.Documents.Storage;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

/// <summary>
/// P1: Platform metadata registries must fail fast on duplicate codes.
/// Silent overwrites make schema validation + generic tooling non-deterministic.
/// </summary>
public sealed class MetadataRegistries_DuplicateCodes_FailFast_P1Tests
{
    [Fact]
    public void DocumentTypeRegistry_DuplicateTypeCode_FailsFast_ButIsIdempotentForSameValue()
    {
        var reg = new DocumentTypeRegistry();

        var m1 = Doc("it_doc", "doc_it_doc");
        var m2 = Doc("IT_DOC", "doc_it_doc__other"); // Same code (case-insensitive), different payload

        reg.Register(m1);

        // Idempotent re-registration of the same metadata value should be allowed.
        var actSame = () => reg.Register(m1);
        actSame.Should().NotThrow();

        // A different metadata object with the same code must fail fast.
        var actDup = () => reg.Register(m2);
        actDup.Should().Throw<NgbConfigurationViolationException>()
            .Which.Message.Should().ContainEquivalentOf("it_doc")
            .And.ContainEquivalentOf("already registered");
    }

    [Fact]
    public void CatalogTypeRegistry_DuplicateCatalogCode_FailsFast_ButIsIdempotentForSameValue()
    {
        var reg = new CatalogTypeRegistry();

        var m1 = Cat("it_cat", "catalog_it_cat");
        var m2 = Cat("IT_CAT", "catalog_it_cat__other"); // Same code (case-insensitive), different payload

        reg.Register(m1);

        var actSame = () => reg.Register(m1);
        actSame.Should().NotThrow();

        var actDup = () => reg.Register(m2);
        actDup.Should().Throw<NgbConfigurationViolationException>()
            .Which.Message.Should().ContainEquivalentOf("it_cat")
            .And.ContainEquivalentOf("already registered");
    }

    private static DocumentTypeMetadata Doc(string typeCode, string tableName)
        => new(
            TypeCode: typeCode,
            Tables: new[]
            {
                new DocumentTableMetadata(
                    TableName: tableName,
                    Kind: TableKind.Head,
                    Columns: Array.Empty<DocumentColumnMetadata>(),
                    Indexes: null)
            });

    private static CatalogTypeMetadata Cat(string catalogCode, string tableName)
        => new(
            CatalogCode: catalogCode,
            DisplayName: "Integration Test Catalog",
            Tables: new[]
            {
                new CatalogTableMetadata(
                    TableName: tableName,
                    Kind: TableKind.Head,
                    Columns: Array.Empty<CatalogColumnMetadata>(),
                    Indexes: Array.Empty<CatalogIndexMetadata>())
            },
            Presentation: new CatalogPresentationMetadata(TableName: tableName, DisplayColumn: "name"),
            Version: new CatalogMetadataVersion(Version: 1, Hash: "it"));
}
