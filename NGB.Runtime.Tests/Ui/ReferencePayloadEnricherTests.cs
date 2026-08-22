using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Hybrid;
using NGB.Metadata.Documents.Storage;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Catalogs.Enrichment;
using NGB.Persistence.Catalogs.Universal;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.Universal;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.Readers.Accounts;
using NGB.Runtime.Ui;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Ui;

public sealed class ReferencePayloadEnricherTests
{
    private const string OwnerCatalogCode = "cat.order";
    private const string PartyCatalogCode = "cat.party";
    private const string OwnerDocumentType = "doc.sale";
    private const string LeaseDocumentType = "doc.lease";
    private const string InvoiceDocumentType = "doc.invoice";

    [Fact]
    public async Task EnrichCatalogItemsAsync_ResolvesSharedCatalogRefsOnceAcrossHeadAndParts()
    {
        var partyA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var partyB = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var catalogTypes = new CatalogTypeRegistry();
        catalogTypes.Register(BuildOwnerCatalogMetadata());
        catalogTypes.Register(BuildPartyCatalogMetadata());

        var documentTypes = new DocumentTypeRegistry();

        var catalogEnrichmentReader = new Mock<ICatalogEnrichmentReader>(MockBehavior.Strict);
        catalogEnrichmentReader
            .Setup(x => x.ResolveManyAsync(
                It.Is<IReadOnlyDictionary<string, IReadOnlyCollection<Guid>>>(batch =>
                    HasCatalogBatch(batch, PartyCatalogCode, partyA, partyB)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new Dictionary<string, IReadOnlyDictionary<Guid, string>>(StringComparer.OrdinalIgnoreCase)
                {
                    [PartyCatalogCode] = new Dictionary<Guid, string>
                    {
                        [partyA] = "Party A",
                        [partyB] = "Party B"
                    }
                });

        var documentDisplayReader = new Mock<IDocumentDisplayReader>(MockBehavior.Strict);
        var accountLookupReader = new Mock<IAccountLookupReader>(MockBehavior.Strict);
        var opregRepo = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);

        var sut = new ReferencePayloadEnricher(
            catalogTypes,
            documentTypes,
            catalogEnrichmentReader.Object,
            documentDisplayReader.Object,
            accountLookupReader.Object,
            opregRepo.Object);

        var item = new CatalogItemDto(
            Id: Guid.NewGuid(),
            Display: "Order",
            Payload: new RecordPayload(
                Fields: new Dictionary<string, JsonElement>
                {
                    ["party_id"] = JsonSerializer.SerializeToElement(partyA)
                },
                Parts: new Dictionary<string, RecordPartPayload>
                {
                    ["lines"] = new(
                    [
                        new Dictionary<string, JsonElement>
                        {
                            ["party_id"] = JsonSerializer.SerializeToElement(partyA)
                        },
                        new Dictionary<string, JsonElement>
                        {
                            ["party_id"] = JsonSerializer.SerializeToElement(partyB)
                        }
                    ])
                }),
            IsMarkedForDeletion: false,
            IsDeleted: false);

        var ownerHead = new CatalogHeadDescriptor(
            CatalogCode: OwnerCatalogCode,
            HeadTableName: "cat_order",
            DisplayColumn: "name",
            Columns:
            [
                new CatalogHeadColumn("party_id", ColumnType.Guid)
            ]);

        var result = await sut.EnrichCatalogItemsAsync(ownerHead, OwnerCatalogCode, [item], CancellationToken.None);

        ReadRef(result[0].Payload.Fields!, "party_id").Should().BeEquivalentTo(new RefValueDto(partyA, "Party A"));
        ReadRef(result[0].Payload.Parts!["lines"].Rows[0], "party_id").Should().BeEquivalentTo(new RefValueDto(partyA, "Party A"));
        ReadRef(result[0].Payload.Parts!["lines"].Rows[1], "party_id").Should().BeEquivalentTo(new RefValueDto(partyB, "Party B"));

        catalogEnrichmentReader.VerifyAll();
        documentDisplayReader.VerifyNoOtherCalls();
        accountLookupReader.VerifyNoOtherCalls();
        opregRepo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnrichDocumentItemsAsync_ResolvesDocumentRefsInSingleBulkCallAcrossHeadAndParts()
    {
        var leaseId = Guid.Parse("10101010-1111-1111-1111-111111111111");
        var invoiceId = Guid.Parse("20202020-2222-2222-2222-222222222222");

        var catalogTypes = new CatalogTypeRegistry();
        var documentTypes = new DocumentTypeRegistry([BuildOwnerDocumentWithDocumentRefsMetadata()]);

        var catalogEnrichmentReader = new Mock<ICatalogEnrichmentReader>(MockBehavior.Strict);
        var documentDisplayReader = new Mock<IDocumentDisplayReader>(MockBehavior.Strict);
        documentDisplayReader
            .Setup(x => x.ResolveRefsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => SameIds(ids, leaseId, invoiceId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new Dictionary<Guid, DocumentDisplayRef>
                {
                    [leaseId] = new(leaseId, LeaseDocumentType, "Lease L-001"),
                    [invoiceId] = new(invoiceId, InvoiceDocumentType, "Invoice INV-77")
                });

        var accountLookupReader = new Mock<IAccountLookupReader>(MockBehavior.Strict);
        var opregRepo = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);

        var sut = new ReferencePayloadEnricher(
            catalogTypes,
            documentTypes,
            catalogEnrichmentReader.Object,
            documentDisplayReader.Object,
            accountLookupReader.Object,
            opregRepo.Object);

        var item = new DocumentDto(
            Id: Guid.NewGuid(),
            Display: "Sale",
            Payload: new RecordPayload(
                Fields: new Dictionary<string, JsonElement>
                {
                    ["lease_id"] = JsonSerializer.SerializeToElement(leaseId)
                },
                Parts: new Dictionary<string, RecordPartPayload>
                {
                    ["lines"] = new(
                    [
                        new Dictionary<string, JsonElement>
                        {
                            ["lease_id"] = JsonSerializer.SerializeToElement(leaseId)
                        },
                        new Dictionary<string, JsonElement>
                        {
                            ["lease_id"] = JsonSerializer.SerializeToElement(invoiceId)
                        }
                    ])
                }),
            Status: DocumentStatus.Draft,
            IsMarkedForDeletion: false,
            Number: "DOC-REF");

        var ownerHead = new DocumentHeadDescriptor(
            TypeCode: OwnerDocumentType,
            HeadTableName: "doc_sale",
            DisplayColumn: "display",
            Columns:
            [
                new DocumentHeadColumn("lease_id", ColumnType.Guid)
            ]);

        var result = await sut.EnrichDocumentItemsAsync(ownerHead, OwnerDocumentType, [item], CancellationToken.None);

        ReadRef(result[0].Payload.Fields!, "lease_id").Should().BeEquivalentTo(new RefValueDto(leaseId, "Lease L-001"));
        ReadRef(result[0].Payload.Parts!["lines"].Rows[1], "lease_id").Should().BeEquivalentTo(new RefValueDto(invoiceId, "Invoice INV-77"));

        documentDisplayReader.VerifyAll();
        catalogEnrichmentReader.VerifyNoOtherCalls();
        accountLookupReader.VerifyNoOtherCalls();
        opregRepo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnrichDocumentItemsAsync_FallsBackToFullGuidWhenDocumentRefIsMissing()
    {
        var missingLeaseId = Guid.Parse("30303030-3333-3333-3333-333333333333");

        var catalogTypes = new CatalogTypeRegistry();
        var documentTypes = new DocumentTypeRegistry([BuildOwnerDocumentWithDocumentRefsMetadata()]);

        var catalogEnrichmentReader = new Mock<ICatalogEnrichmentReader>(MockBehavior.Strict);
        var documentDisplayReader = new Mock<IDocumentDisplayReader>(MockBehavior.Strict);
        documentDisplayReader
            .Setup(x => x.ResolveRefsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => SameIds(ids, missingLeaseId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new Dictionary<Guid, DocumentDisplayRef>
                {
                    [missingLeaseId] = new(missingLeaseId, string.Empty, missingLeaseId.ToString("N")[..8])
                });

        var accountLookupReader = new Mock<IAccountLookupReader>(MockBehavior.Strict);
        var opregRepo = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);

        var sut = new ReferencePayloadEnricher(
            catalogTypes,
            documentTypes,
            catalogEnrichmentReader.Object,
            documentDisplayReader.Object,
            accountLookupReader.Object,
            opregRepo.Object);

        var item = new DocumentDto(
            Id: Guid.NewGuid(),
            Display: "Sale",
            Payload: new RecordPayload(
                Fields: new Dictionary<string, JsonElement>
                {
                    ["lease_id"] = JsonSerializer.SerializeToElement(missingLeaseId)
                }),
            Status: DocumentStatus.Draft,
            IsMarkedForDeletion: false,
            Number: "DOC-MISSING");

        var ownerHead = new DocumentHeadDescriptor(
            TypeCode: OwnerDocumentType,
            HeadTableName: "doc_sale",
            DisplayColumn: "display",
            Columns:
            [
                new DocumentHeadColumn("lease_id", ColumnType.Guid)
            ]);

        var result = await sut.EnrichDocumentItemsAsync(ownerHead, OwnerDocumentType, [item], CancellationToken.None);

        ReadRef(result[0].Payload.Fields!, "lease_id").Should().BeEquivalentTo(new RefValueDto(missingLeaseId, missingLeaseId.ToString()));

        documentDisplayReader.VerifyAll();
        catalogEnrichmentReader.VerifyNoOtherCalls();
        accountLookupReader.VerifyNoOtherCalls();
        opregRepo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnrichDocumentItemsAsync_UsesTargetedAccountAndRegisterLookupsOnceAcrossHeadAndParts()
    {
        var accountA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var accountB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var registerA = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var registerB = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        var catalogTypes = new CatalogTypeRegistry();
        var documentTypes = new DocumentTypeRegistry([BuildOwnerDocumentMetadata()]);

        var catalogEnrichmentReader = new Mock<ICatalogEnrichmentReader>(MockBehavior.Strict);
        var documentDisplayReader = new Mock<IDocumentDisplayReader>(MockBehavior.Strict);
        var accountLookupReader = new Mock<IAccountLookupReader>(MockBehavior.Strict);
        accountLookupReader
            .Setup(x => x.GetByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => SameIds(ids, accountA, accountB)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new AccountLookupRecord { AccountId = accountA, Code = "1010", Name = "Cash" },
                new AccountLookupRecord { AccountId = accountB, Code = "2020", Name = "Receivable" }
            ]);

        var opregRepo = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        opregRepo
            .Setup(x => x.GetByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => SameIds(ids, registerA, registerB)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new OperationalRegisterAdminItem(registerA, "WAREHOUSE", "warehouse", "warehouse", "Warehouse", true, DateTime.UnixEpoch, DateTime.UnixEpoch),
                new OperationalRegisterAdminItem(registerB, "TRANSIT", "transit", "transit", "Transit", true, DateTime.UnixEpoch, DateTime.UnixEpoch)
            ]);

        var sut = new ReferencePayloadEnricher(
            catalogTypes,
            documentTypes,
            catalogEnrichmentReader.Object,
            documentDisplayReader.Object,
            accountLookupReader.Object,
            opregRepo.Object);

        var item = new DocumentDto(
            Id: Guid.NewGuid(),
            Display: "Sale",
            Payload: new RecordPayload(
                Fields: new Dictionary<string, JsonElement>
                {
                    ["counter_account_id"] = JsonSerializer.SerializeToElement(accountA),
                    ["warehouse_register_id"] = JsonSerializer.SerializeToElement(registerA)
                },
                Parts: new Dictionary<string, RecordPartPayload>
                {
                    ["lines"] = new(
                    [
                        new Dictionary<string, JsonElement>
                        {
                            ["counter_account_id"] = JsonSerializer.SerializeToElement(accountA),
                            ["warehouse_register_id"] = JsonSerializer.SerializeToElement(registerA)
                        },
                        new Dictionary<string, JsonElement>
                        {
                            ["counter_account_id"] = JsonSerializer.SerializeToElement(accountB),
                            ["warehouse_register_id"] = JsonSerializer.SerializeToElement(registerB)
                        }
                    ])
                }),
            Status: DocumentStatus.Draft,
            IsMarkedForDeletion: false,
            Number: "DOC-1");

        var ownerHead = new DocumentHeadDescriptor(
            TypeCode: OwnerDocumentType,
            HeadTableName: "doc_sale",
            DisplayColumn: "display",
            Columns:
            [
                new DocumentHeadColumn("counter_account_id", ColumnType.Guid),
                new DocumentHeadColumn("warehouse_register_id", ColumnType.Guid)
            ]);

        var result = await sut.EnrichDocumentItemsAsync(ownerHead, OwnerDocumentType, [item], CancellationToken.None);

        ReadRef(result[0].Payload.Fields!, "counter_account_id").Should().BeEquivalentTo(new RefValueDto(accountA, "1010 — Cash"));
        ReadRef(result[0].Payload.Fields!, "warehouse_register_id").Should().BeEquivalentTo(new RefValueDto(registerA, "WAREHOUSE — Warehouse"));
        ReadRef(result[0].Payload.Parts!["lines"].Rows[1], "counter_account_id").Should().BeEquivalentTo(new RefValueDto(accountB, "2020 — Receivable"));
        ReadRef(result[0].Payload.Parts!["lines"].Rows[1], "warehouse_register_id").Should().BeEquivalentTo(new RefValueDto(registerB, "TRANSIT — Transit"));

        accountLookupReader.VerifyAll();
        opregRepo.VerifyAll();
        catalogEnrichmentReader.VerifyNoOtherCalls();
        documentDisplayReader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnrichMethods_ReturnOriginalCollections_WhenEmptyOrNoReferenceWorkExists()
    {
        var sut = new ReferencePayloadEnricher(
            new CatalogTypeRegistry(),
            new DocumentTypeRegistry(),
            Mock.Of<ICatalogEnrichmentReader>(),
            Mock.Of<IDocumentDisplayReader>(),
            Mock.Of<IAccountLookupReader>(),
            Mock.Of<IOperationalRegisterRepository>());
        var catalogHead = new CatalogHeadDescriptor(
            "missing.catalog",
            "cat_missing",
            "name",
            [
                new("name", ColumnType.String),
                new("plain_guid", ColumnType.Guid),
                new("catalog_id", ColumnType.Guid),
                new("document_id", ColumnType.Guid),
                new("_id", ColumnType.Guid),
                new("unknown_id", ColumnType.Guid)
            ]);
        var documentHead = new DocumentHeadDescriptor(
            "missing.document",
            "doc_missing",
            "display",
            [
                new("name", ColumnType.String),
                new("plain_guid", ColumnType.Guid),
                new("catalog_id", ColumnType.Guid),
                new("document_id", ColumnType.Guid),
                new("_id", ColumnType.Guid),
                new("unknown_id", ColumnType.Guid)
            ]);
        IReadOnlyList<CatalogItemDto> emptyCatalogs = [];
        IReadOnlyList<DocumentDto> emptyDocuments = [];

        (await sut.EnrichCatalogItemsAsync(catalogHead, catalogHead.CatalogCode, emptyCatalogs, CancellationToken.None))
            .Should().BeSameAs(emptyCatalogs);
        (await sut.EnrichDocumentItemsAsync(documentHead, documentHead.TypeCode, emptyDocuments, CancellationToken.None))
            .Should().BeSameAs(emptyDocuments);

        IReadOnlyList<CatalogItemDto> catalogs = [CatalogItem(new RecordPayload())];
        IReadOnlyList<DocumentDto> documents = [DocumentItem(new RecordPayload())];
        (await sut.EnrichCatalogItemsAsync(catalogHead, catalogHead.CatalogCode, catalogs, CancellationToken.None))
            .Should().BeSameAs(catalogs);
        (await sut.EnrichDocumentItemsAsync(documentHead, documentHead.TypeCode, documents, CancellationToken.None))
            .Should().BeSameAs(documents);
    }

    [Fact]
    public async Task EnrichMethods_IgnoreTechnicalJsonAndNonReferencePartColumns()
    {
        const string catalogCode = "cat.filtered";
        const string documentCode = "doc.filtered";
        var catalogTypes = new CatalogTypeRegistry();
        catalogTypes.Register(new CatalogTypeMetadata(
            catalogCode,
            "Filtered",
            [
                new CatalogTableMetadata("cat_filtered", TableKind.Head, [], []),
                new CatalogTableMetadata(
                    TableName: "cat_filtered__rows",
                    Kind: TableKind.Part,
                    Columns:
                    [
                        new CatalogColumnMetadata("catalog_id", ColumnType.Guid),
                        new CatalogColumnMetadata("json_id", ColumnType.Json),
                        new CatalogColumnMetadata("name", ColumnType.String)
                    ],
                    Indexes: [],
                    PartCode: "rows")
            ],
            new CatalogPresentationMetadata("cat_filtered", "name"),
            new CatalogMetadataVersion(1, "tests")));
        var documentTypes = new DocumentTypeRegistry([
            new DocumentTypeMetadata(
                documentCode,
                [
                    new DocumentTableMetadata("doc_filtered", TableKind.Head, []),
                    new DocumentTableMetadata(
                        TableName: "doc_filtered__rows",
                        Kind: TableKind.Part,
                        Columns:
                        [
                            new DocumentColumnMetadata("document_id", ColumnType.Guid),
                            new DocumentColumnMetadata("json_id", ColumnType.Json),
                            new DocumentColumnMetadata("name", ColumnType.String)
                        ],
                        PartCode: "rows")
                ],
                new DocumentPresentationMetadata("Filtered"))
        ]);
        var sut = new ReferencePayloadEnricher(
            catalogTypes,
            documentTypes,
            Mock.Of<ICatalogEnrichmentReader>(),
            Mock.Of<IDocumentDisplayReader>(),
            Mock.Of<IAccountLookupReader>(),
            Mock.Of<IOperationalRegisterRepository>());
        IReadOnlyList<CatalogItemDto> catalogs = [CatalogItem(new RecordPayload())];
        IReadOnlyList<DocumentDto> documents = [DocumentItem(new RecordPayload())];

        var catalogResult = await sut.EnrichCatalogItemsAsync(
            new CatalogHeadDescriptor(catalogCode, "cat_filtered", "name", [new("unmapped", ColumnType.String)]),
            catalogCode,
            catalogs,
            CancellationToken.None);
        var documentResult = await sut.EnrichDocumentItemsAsync(
            new DocumentHeadDescriptor(documentCode, "doc_filtered", "display", [new("unmapped", ColumnType.String)]),
            documentCode,
            documents,
            CancellationToken.None);

        catalogResult.Should().BeSameAs(catalogs);
        documentResult.Should().BeSameAs(documents);
    }

    [Fact]
    public async Task EnrichDocumentItemsAsync_UsesCatalogAndDocumentNameHeuristicsIncludingAmbiguity()
    {
        var partyId = Guid.NewGuid();
        var directId = Guid.NewGuid();
        var leaseId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var ambiguousId = Guid.NewGuid();
        var catalogTypes = new CatalogTypeRegistry();
        foreach (var code in new[] { "cat.party", "direct", "cat.ambiguous", "other.ambiguous" })
            catalogTypes.Register(SimpleCatalogMetadata(code));

        var documentTypes = new DocumentTypeRegistry(
            new[] { "doc.lease", "doc.sales_invoice", "doc.ambiguous" }.Select(SimpleDocumentMetadata));
        var catalogReader = new Mock<ICatalogEnrichmentReader>(MockBehavior.Strict);
        catalogReader.Setup(x => x.ResolveManyAsync(
                It.Is<IReadOnlyDictionary<string, IReadOnlyCollection<Guid>>>(batch => HasCatalogPairBatch(batch, partyId, directId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IReadOnlyDictionary<Guid, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["cat.party"] = new Dictionary<Guid, string> { [partyId] = "Party" },
                ["direct"] = new Dictionary<Guid, string> { [directId] = "Direct" }
            });
        var documentReader = new Mock<IDocumentDisplayReader>(MockBehavior.Strict);
        documentReader.Setup(x => x.ResolveRefsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => SameIds(ids, leaseId, invoiceId, ambiguousId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DocumentDisplayRef>
            {
                [leaseId] = new(leaseId, "doc.lease", "Lease"),
                [invoiceId] = new(invoiceId, "doc.sales_invoice", "Invoice"),
                [ambiguousId] = new(ambiguousId, "doc.ambiguous", "Ambiguous document")
            });
        var sut = new ReferencePayloadEnricher(
            catalogTypes,
            documentTypes,
            catalogReader.Object,
            documentReader.Object,
            Mock.Of<IAccountLookupReader>(),
            Mock.Of<IOperationalRegisterRepository>());
        var payload = new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["party_id"] = JsonSerializer.SerializeToElement(new { id = partyId }),
            ["direct_id"] = JsonSerializer.SerializeToElement(directId),
            ["customer_lease_id"] = JsonSerializer.SerializeToElement(new { Id = leaseId }),
            ["invoice_id"] = JsonSerializer.SerializeToElement(invoiceId),
            ["ambiguous_id"] = JsonSerializer.SerializeToElement(ambiguousId)
        });
        var head = new DocumentHeadDescriptor(
            "doc.unregistered_owner",
            "doc_owner",
            "display",
            payload.Fields!.Keys.Select(x => new DocumentHeadColumn(x, ColumnType.Guid)).ToList());

        var result = await sut.EnrichDocumentItemsAsync(
            head,
            head.TypeCode,
            [DocumentItem(payload)],
            CancellationToken.None);

        ReadRef(result[0].Payload.Fields!, "party_id").Display.Should().Be("Party");
        ReadRef(result[0].Payload.Fields!, "direct_id").Display.Should().Be("Direct");
        ReadRef(result[0].Payload.Fields!, "customer_lease_id").Display.Should().Be("Lease");
        ReadRef(result[0].Payload.Fields!, "invoice_id").Display.Should().Be("Invoice");
        ReadRef(result[0].Payload.Fields!, "ambiguous_id").Display.Should().Be("Ambiguous document");
        catalogReader.VerifyAll();
        documentReader.VerifyAll();
    }

    [Fact]
    public async Task EnrichDocumentItemsAsync_ToleratesMalformedReferencesAndPreservesUnchangedRows()
    {
        var namedAccountId = Guid.NewGuid();
        var missingAccountId = Guid.NewGuid();
        var partAccountId = Guid.NewGuid();
        var namedRegisterId = Guid.NewGuid();
        var missingRegisterId = Guid.NewGuid();
        var metadata = new DocumentTypeMetadata(
            "doc.edge",
            [
                new DocumentTableMetadata(
                    "doc_edge",
                    TableKind.Head,
                    [new DocumentColumnMetadata("ledger_id", ColumnType.Guid, Lookup: new ChartOfAccountsLookupSourceMetadata())]),
                new DocumentTableMetadata(
                    TableName: "doc_edge__lines",
                    Kind: TableKind.Part,
                    Columns:
                    [
                        new DocumentColumnMetadata("part_account_id", ColumnType.Guid),
                        new DocumentColumnMetadata("document_id", ColumnType.Guid),
                        new DocumentColumnMetadata("json_id", ColumnType.Json)
                    ],
                    PartCode: "lines")
            ],
            new DocumentPresentationMetadata("Edge"));
        var accountReader = new Mock<IAccountLookupReader>(MockBehavior.Strict);
        accountReader.Setup(x => x.GetByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => SameIds(ids, namedAccountId, missingAccountId, partAccountId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AccountLookupRecord { AccountId = namedAccountId, Code = " ", Name = "Named account" },
                new AccountLookupRecord { AccountId = partAccountId, Code = "3000", Name = "Part account" }
            ]);
        var registerReader = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        registerReader.Setup(x => x.GetByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => SameIds(ids, namedRegisterId, missingRegisterId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new OperationalRegisterAdminItem(namedRegisterId, " ", "named", "named", "Named register", true, DateTime.UnixEpoch, DateTime.UnixEpoch)
            ]);
        var sut = new ReferencePayloadEnricher(
            new CatalogTypeRegistry(),
            new DocumentTypeRegistry([metadata]),
            Mock.Of<ICatalogEnrichmentReader>(),
            Mock.Of<IDocumentDisplayReader>(),
            accountReader.Object,
            registerReader.Object);
        var malformedNull = JsonSerializer.SerializeToElement<string?>(null);
        var fields = new Dictionary<string, JsonElement>
        {
            ["ledger_id"] = JsonSerializer.SerializeToElement(namedAccountId),
            ["missing_account_id"] = JsonSerializer.SerializeToElement(missingAccountId),
            ["lower_account_id"] = JsonSerializer.SerializeToElement(new { id = namedAccountId }),
            ["pascal_account_id"] = JsonSerializer.SerializeToElement(new { Id = namedAccountId }),
            ["null_account_id"] = malformedNull,
            ["undefined_account_id"] = default,
            ["invalid_account_id"] = JsonSerializer.SerializeToElement("not-a-guid"),
            ["number_account_id"] = JsonSerializer.SerializeToElement(42),
            ["bad_lower_account_id"] = JsonSerializer.SerializeToElement(new { id = 42 }),
            ["bad_pascal_account_id"] = JsonSerializer.SerializeToElement(new { Id = 42 }),
            ["empty_object_account_id"] = JsonSerializer.SerializeToElement(new { }),
            ["warehouse_register_id"] = JsonSerializer.SerializeToElement(namedRegisterId),
            ["missing_register_id"] = JsonSerializer.SerializeToElement(missingRegisterId)
        };
        var partRows = new List<IReadOnlyDictionary<string, JsonElement>>
        {
            new Dictionary<string, JsonElement>(),
            new Dictionary<string, JsonElement> { ["part_account_id"] = malformedNull },
            new Dictionary<string, JsonElement> { ["part_account_id"] = JsonSerializer.SerializeToElement(partAccountId) }
        };
        var complexPayload = new RecordPayload(
            fields,
            new Dictionary<string, RecordPartPayload>
            {
                ["unknown"] = new([new Dictionary<string, JsonElement>()]),
                ["lines"] = new(partRows)
            });
        var partsOnlyPayload = new RecordPayload(
            Parts: new Dictionary<string, RecordPartPayload>
            {
                ["lines"] = new([
                    new Dictionary<string, JsonElement> { ["part_account_id"] = JsonSerializer.SerializeToElement(partAccountId) }
                ])
            });
        var invalidPartsOnlyPayload = new RecordPayload(
            Parts: new Dictionary<string, RecordPartPayload>
            {
                ["lines"] = new([
                    new Dictionary<string, JsonElement>(),
                    new Dictionary<string, JsonElement> { ["part_account_id"] = malformedNull }
                ])
            });
        var headColumns = fields.Keys
            .Append("absent_account_id")
            .Select(x => new DocumentHeadColumn(x, ColumnType.Guid))
            .ToList();
        var items = new[]
        {
            DocumentItem(complexPayload),
            DocumentItem(new RecordPayload()),
            DocumentItem(new RecordPayload(new Dictionary<string, JsonElement>(), new Dictionary<string, RecordPartPayload>())),
            DocumentItem(partsOnlyPayload),
            DocumentItem(invalidPartsOnlyPayload)
        };

        var result = await sut.EnrichDocumentItemsAsync(
            new DocumentHeadDescriptor("doc.edge", "doc_edge", "display", headColumns),
            "doc.edge",
            items,
            CancellationToken.None);

        ReadRef(result[0].Payload.Fields!, "ledger_id").Display.Should().Be("Named account");
        ReadRef(result[0].Payload.Fields!, "missing_account_id").Display.Should().Be(missingAccountId.ToString());
        ReadRef(result[0].Payload.Fields!, "warehouse_register_id").Display.Should().Be("Named register");
        ReadRef(result[0].Payload.Fields!, "missing_register_id").Display.Should().Be(missingRegisterId.ToString());
        result[0].Payload.Fields!["null_account_id"].ValueKind.Should().Be(JsonValueKind.Null);
        result[0].Payload.Fields!["undefined_account_id"].ValueKind.Should().Be(JsonValueKind.Undefined);
        result[0].Payload.Parts!["lines"].Rows[0].Should().BeEmpty();
        result[0].Payload.Parts!["lines"].Rows[1]["part_account_id"].ValueKind.Should().Be(JsonValueKind.Null);
        ReadRef(result[0].Payload.Parts!["lines"].Rows[2], "part_account_id").Display.Should().Be("3000 — Part account");
        ReadRef(result[3].Payload.Parts!["lines"].Rows[0], "part_account_id").Display.Should().Be("3000 — Part account");
        accountReader.VerifyAll();
        registerReader.VerifyAll();
    }

    [Fact]
    public async Task EnrichCatalogItemsAsync_CatalogDisplayResolutionCoversEveryFallback()
    {
        const string ownerCode = "cat.fallback_owner";
        const string missingType = "cat.missing_labels";
        const string presentType = "cat.present_labels";
        var missingTypeId = Guid.NewGuid();
        var absentId = Guid.NewGuid();
        var blankId = Guid.NewGuid();
        var guidDisplayId = Guid.NewGuid();
        var goodId = Guid.NewGuid();
        var columns = new[]
        {
            new CatalogColumnMetadata("missing_type_id", ColumnType.Guid, Lookup: new CatalogLookupSourceMetadata(missingType)),
            new CatalogColumnMetadata("absent_id", ColumnType.Guid, Lookup: new CatalogLookupSourceMetadata(presentType)),
            new CatalogColumnMetadata("blank_id", ColumnType.Guid, Lookup: new CatalogLookupSourceMetadata(presentType)),
            new CatalogColumnMetadata("guid_display_id", ColumnType.Guid, Lookup: new CatalogLookupSourceMetadata(presentType)),
            new CatalogColumnMetadata("good_id", ColumnType.Guid, Lookup: new CatalogLookupSourceMetadata(presentType))
        };
        var ownerMetadata = new CatalogTypeMetadata(
            ownerCode,
            "Fallback owner",
            [new CatalogTableMetadata("cat_fallback_owner", TableKind.Head, columns, [])],
            new CatalogPresentationMetadata("cat_fallback_owner", "name"),
            new CatalogMetadataVersion(1, "tests"));
        var catalogTypes = new CatalogTypeRegistry();
        catalogTypes.Register(ownerMetadata);
        catalogTypes.Register(SimpleCatalogMetadata(missingType));
        catalogTypes.Register(SimpleCatalogMetadata(presentType));
        var catalogReader = new Mock<ICatalogEnrichmentReader>(MockBehavior.Strict);
        catalogReader.Setup(x => x.ResolveManyAsync(
                It.Is<IReadOnlyDictionary<string, IReadOnlyCollection<Guid>>>(batch =>
                    HasFallbackCatalogBatch(batch, missingTypeId, absentId, blankId, guidDisplayId, goodId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IReadOnlyDictionary<Guid, string>>(StringComparer.OrdinalIgnoreCase)
            {
                [presentType] = new Dictionary<Guid, string>
                {
                    [blankId] = " ",
                    [guidDisplayId] = guidDisplayId.ToString(),
                    [goodId] = "Resolved"
                }
            });
        var sut = new ReferencePayloadEnricher(
            catalogTypes,
            new DocumentTypeRegistry(),
            catalogReader.Object,
            Mock.Of<IDocumentDisplayReader>(),
            Mock.Of<IAccountLookupReader>(),
            Mock.Of<IOperationalRegisterRepository>());
        var values = new[] { missingTypeId, absentId, blankId, guidDisplayId, goodId };
        var payload = new RecordPayload(columns.Zip(values).ToDictionary(
            x => x.First.ColumnName,
            x => JsonSerializer.SerializeToElement(x.Second)));

        var result = await sut.EnrichCatalogItemsAsync(
            new CatalogHeadDescriptor(
                ownerCode,
                "cat_fallback_owner",
                "name",
                columns.Select(x => new CatalogHeadColumn(x.ColumnName, x.ColumnType)).ToList()),
            ownerCode,
            [CatalogItem(payload)],
            CancellationToken.None);

        ReadRef(result[0].Payload.Fields!, "missing_type_id").Display.Should().Be(missingTypeId.ToString());
        ReadRef(result[0].Payload.Fields!, "absent_id").Display.Should().Be(absentId.ToString());
        ReadRef(result[0].Payload.Fields!, "blank_id").Display.Should().Be(blankId.ToString());
        ReadRef(result[0].Payload.Fields!, "guid_display_id").Display.Should().Be(guidDisplayId.ToString());
        ReadRef(result[0].Payload.Fields!, "good_id").Display.Should().Be("Resolved");
        catalogReader.VerifyAll();
    }

    [Fact]
    public async Task EnrichMethods_HandleMetadataWithoutHeadTablesAndRejectUnsupportedLookupMetadata()
    {
        const string catalogCode = "cat.part_only";
        const string documentCode = "doc.part_only";
        var catalogTypes = new CatalogTypeRegistry();
        catalogTypes.Register(new CatalogTypeMetadata(
            catalogCode,
            "Part only",
            [new CatalogTableMetadata(
                TableName: "cat_part_only__rows",
                Kind: TableKind.Part,
                Columns: [new CatalogColumnMetadata("name", ColumnType.String)],
                Indexes: [],
                PartCode: "rows")],
            new CatalogPresentationMetadata("cat_part_only", "name"),
            new CatalogMetadataVersion(1, "tests")));
        var documentTypes = new DocumentTypeRegistry([
            new DocumentTypeMetadata(
                documentCode,
                [new DocumentTableMetadata(
                    TableName: "doc_part_only__rows",
                    Kind: TableKind.Part,
                    Columns: [new DocumentColumnMetadata("name", ColumnType.String)],
                    PartCode: "rows")],
                new DocumentPresentationMetadata("Part only"))
        ]);
        var sut = new ReferencePayloadEnricher(
            catalogTypes,
            documentTypes,
            Mock.Of<ICatalogEnrichmentReader>(),
            Mock.Of<IDocumentDisplayReader>(),
            Mock.Of<IAccountLookupReader>(),
            Mock.Of<IOperationalRegisterRepository>());
        IReadOnlyList<CatalogItemDto> catalogs = [CatalogItem(new RecordPayload())];
        IReadOnlyList<DocumentDto> documents = [DocumentItem(new RecordPayload())];

        (await sut.EnrichCatalogItemsAsync(
                new CatalogHeadDescriptor(catalogCode, "cat_part_only", "name", [new(" ", ColumnType.Guid)]),
                catalogCode,
                catalogs,
                CancellationToken.None))
            .Should().BeSameAs(catalogs);
        (await sut.EnrichDocumentItemsAsync(
                new DocumentHeadDescriptor(documentCode, "doc_part_only", "display", [new(" ", ColumnType.Guid)]),
                documentCode,
                documents,
                CancellationToken.None))
            .Should().BeSameAs(documents);

        const string invalidCode = "cat.invalid_lookup";
        catalogTypes.Register(new CatalogTypeMetadata(
            invalidCode,
            "Invalid",
            [new CatalogTableMetadata(
                "cat_invalid_lookup",
                TableKind.Head,
                [new CatalogColumnMetadata("target_id", ColumnType.Guid, Lookup: new UnsupportedLookupSourceMetadata())],
                [])],
            new CatalogPresentationMetadata("cat_invalid_lookup", "name"),
            new CatalogMetadataVersion(1, "tests")));

        await sut.Invoking(enricher => enricher.EnrichCatalogItemsAsync(
                new CatalogHeadDescriptor(invalidCode, "cat_invalid_lookup", "name", [new("target_id", ColumnType.Guid)]),
                invalidCode,
                catalogs,
                CancellationToken.None))
            .Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*Unsupported lookup source metadata type*");
    }

    private static CatalogTypeMetadata BuildOwnerCatalogMetadata()
        => new(
            CatalogCode: OwnerCatalogCode,
            DisplayName: "Order",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_order",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new CatalogColumnMetadata("party_id", ColumnType.Guid, Lookup: new CatalogLookupSourceMetadata(PartyCatalogCode))
                    ],
                    Indexes: []),
                new CatalogTableMetadata(
                    TableName: "cat_order__lines",
                    Kind: TableKind.Part,
                    PartCode: "lines",
                    Columns:
                    [
                        new CatalogColumnMetadata("party_id", ColumnType.Guid, Lookup: new CatalogLookupSourceMetadata(PartyCatalogCode))
                    ],
                    Indexes: [])
            ],
            Presentation: new CatalogPresentationMetadata("cat_order", "name"),
            Version: new CatalogMetadataVersion(1, "tests"));

    private static CatalogTypeMetadata BuildPartyCatalogMetadata()
        => new(
            CatalogCode: PartyCatalogCode,
            DisplayName: "Party",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_party",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new CatalogColumnMetadata("name", ColumnType.String)
                    ],
                    Indexes: [])
            ],
            Presentation: new CatalogPresentationMetadata("cat_party", "name"),
            Version: new CatalogMetadataVersion(1, "tests"));

    private static DocumentTypeMetadata BuildOwnerDocumentMetadata()
        => new(
            TypeCode: OwnerDocumentType,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_sale",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new DocumentColumnMetadata("counter_account_id", ColumnType.Guid),
                        new DocumentColumnMetadata("warehouse_register_id", ColumnType.Guid)
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_sale__lines",
                    Kind: TableKind.Part,
                    PartCode: "lines",
                    Columns:
                    [
                        new DocumentColumnMetadata("counter_account_id", ColumnType.Guid),
                        new DocumentColumnMetadata("warehouse_register_id", ColumnType.Guid)
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Sale"));

    private static DocumentTypeMetadata BuildOwnerDocumentWithDocumentRefsMetadata()
        => new(
            TypeCode: OwnerDocumentType,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_sale",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new DocumentColumnMetadata(
                            "lease_id",
                            ColumnType.Guid,
                            Lookup: new DocumentLookupSourceMetadata([LeaseDocumentType, InvoiceDocumentType]))
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_sale__lines",
                    Kind: TableKind.Part,
                    PartCode: "lines",
                    Columns:
                    [
                        new DocumentColumnMetadata(
                            "lease_id",
                            ColumnType.Guid,
                            Lookup: new DocumentLookupSourceMetadata([LeaseDocumentType, InvoiceDocumentType]))
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Sale"));

    private static CatalogTypeMetadata SimpleCatalogMetadata(string code)
        => new(
            code,
            code,
            [new CatalogTableMetadata($"cat_{code.Replace('.', '_')}", TableKind.Head, [], [])],
            new CatalogPresentationMetadata($"cat_{code.Replace('.', '_')}", "name"),
            new CatalogMetadataVersion(1, "tests"));

    private static DocumentTypeMetadata SimpleDocumentMetadata(string code)
        => new(
            code,
            [new DocumentTableMetadata($"doc_{code.Replace('.', '_')}", TableKind.Head, [])],
            new DocumentPresentationMetadata(code));

    private static CatalogItemDto CatalogItem(RecordPayload payload)
        => new(Guid.NewGuid(), "Catalog", payload, IsMarkedForDeletion: false, IsDeleted: false);

    private static DocumentDto DocumentItem(RecordPayload payload)
        => new(Guid.NewGuid(), "Document", payload, DocumentStatus.Draft, IsMarkedForDeletion: false, Number: "DOC");

    private sealed record UnsupportedLookupSourceMetadata : LookupSourceMetadata;

    private static bool SameIds(IEnumerable<Guid> actual, params Guid[] expected)
        => actual.OrderBy(x => x).SequenceEqual(expected.OrderBy(x => x));

    private static bool HasCatalogBatch(
        IReadOnlyDictionary<string, IReadOnlyCollection<Guid>> batch,
        string catalogCode,
        params Guid[] expectedIds)
        => batch.Count == 1
           && batch.TryGetValue(catalogCode, out var ids)
           && SameIds(ids, expectedIds);

    private static bool HasCatalogPairBatch(
        IReadOnlyDictionary<string, IReadOnlyCollection<Guid>> batch,
        Guid partyId,
        Guid directId)
        => batch.Count == 2
           && batch.TryGetValue("cat.party", out var partyIds)
           && SameIds(partyIds, partyId)
           && batch.TryGetValue("direct", out var directIds)
           && SameIds(directIds, directId);

    private static bool HasFallbackCatalogBatch(
        IReadOnlyDictionary<string, IReadOnlyCollection<Guid>> batch,
        Guid missingTypeId,
        Guid absentId,
        Guid blankId,
        Guid guidDisplayId,
        Guid goodId)
        => batch.Count == 2
           && batch.TryGetValue("cat.missing_labels", out var missingIds)
           && SameIds(missingIds, missingTypeId)
           && batch.TryGetValue("cat.present_labels", out var presentIds)
           && SameIds(presentIds, absentId, blankId, guidDisplayId, goodId);

    private static RefValueDto ReadRef(IReadOnlyDictionary<string, JsonElement> fields, string key)
    {
        var element = fields[key];
        var id = Guid.Parse(element.GetProperty("id").GetString()!);
        var display = element.GetProperty("display").GetString()!;
        return new RefValueDto(id, display);
    }
}
