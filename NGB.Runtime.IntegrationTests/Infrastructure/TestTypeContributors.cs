using NGB.Definitions;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Documents.Hybrid;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

internal sealed class TestDocumentContributor : IDefinitionsContributor
{
    private static readonly (string Code, string DisplayName)[] Documents =
    {
        ("it_doc_a", "IT Document A"),
        ("it_doc_tx", "IT Document TX"),
        ("it_doc_ts", "IT Document TypedStorage"),
        ("it_doc_ts_ext", "IT Document TypedStorage ExternalTx"),
        ("it_doc_audit_rollback", "IT Document Audit Rollback"),
        ("it_num_req", "IT Document Numbering RequestValidation"),
        ("it_alpha",  "IT Document Alpha"),
        ("it_beta",  "IT Document Beta"),
        ("foo", "Foo"),
        ("foo_bar", "Foo Bar"),
        ("foo_bar_baz", "Foo Bar Baz"),
        ("demo.sales_invoice",  "Demo SalesInvoice"),
        ("test_doc",  "Test Document"),
        ("test", "Test"),
        ("doc_test", "Doc Test"),
        ("it_doc_del", "IT Document DeleteDraft"),
    };

    public void Contribute(DefinitionsBuilder builder)
    {
        foreach (var d in Documents)
        {
            builder.AddDocument(d.Code, b => b.Metadata(new DocumentTypeMetadata(
                d.Code,
                Array.Empty<DocumentTableMetadata>(),
                new DocumentPresentationMetadata(d.DisplayName),
                new DocumentMetadataVersion(1, "it-tests"))));
        }
    }
}

internal sealed class TestCatalogContributor : IDefinitionsContributor
{
    private static readonly (string Code, string DisplayName)[] Catalogs =
    {
        ("it_cat_tx", "IT Catalog TX"),
        ("it_cat_ts", "IT Catalog TypedStorage"),
        ("it_cat_ts_ext", "IT Catalog TypedStorage ExternalTx"),
        ("it_cat_conc", "IT Catalog Concurrency"),
        ("it_cat_aud_rb", "IT Catalog Audit Rollback"),
        ("it_cat_audit_create", "IT Catalog Audit Create"),
        ("it_cat_audit_delete", "IT Catalog Audit Delete"),
        ("it_cat_audit_fail_create", "IT Catalog Audit Fail Create"),
        ("it_cat_audit_fail_delete", "IT Catalog Audit Fail Delete"),
        ("it_cat_audit_noop", "IT Catalog Audit NoOp"),
        ("test_catalog", "Test Catalog"),
        ("test_catalog_2", "Test Catalog 2"),
    };

    public void Contribute(DefinitionsBuilder builder)
    {
        foreach (var c in Catalogs)
        {
            builder.AddCatalog(c.Code, b => b.Metadata(new CatalogTypeMetadata(
                c.Code,
                c.DisplayName,
                Array.Empty<CatalogTableMetadata>(),
                new CatalogPresentationMetadata($"cat_{c.Code}", "name"),
                new CatalogMetadataVersion(1, "it-tests"))));
        }
    }
}
