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

    private static bool SameIds(IEnumerable<Guid> actual, params Guid[] expected)
        => actual.OrderBy(x => x).SequenceEqual(expected.OrderBy(x => x));

    private static bool HasCatalogBatch(
        IReadOnlyDictionary<string, IReadOnlyCollection<Guid>> batch,
        string catalogCode,
        params Guid[] expectedIds)
        => batch.Count == 1
           && batch.TryGetValue(catalogCode, out var ids)
           && SameIds(ids, expectedIds);

    private static RefValueDto ReadRef(IReadOnlyDictionary<string, JsonElement> fields, string key)
    {
        var element = fields[key];
        var id = Guid.Parse(element.GetProperty("id").GetString()!);
        var display = element.GetProperty("display").GetString()!;
        return new RefValueDto(id, display);
    }
}
