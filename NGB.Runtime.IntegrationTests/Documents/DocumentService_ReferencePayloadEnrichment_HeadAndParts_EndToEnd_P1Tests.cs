using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentService_ReferencePayloadEnrichment_HeadAndParts_EndToEnd_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string PartyCatalogCode = "it_ref_party_doc";
    private const string LeaseDocumentCode = "it_ref_lease_doc";
    private const string OwnerDocumentCode = "it_doc_ref_payload";

    [Fact]
    public async Task GetByIdAsync_EnrichesHeadAndPartReferenceFields_AndAcceptsGuidOrRefPayloads()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, ReferencePayloadDocumentContributor>());

        await EnsureTablesAsync(host);
        var accountId = await CreateAccountAsync(host, code: "1100", name: "Accounts Receivable - Tenants", AccountType.Asset, StatementSection.Assets);

        await using var scope = host.Services.CreateAsyncScope();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var party = await catalogs.CreateAsync(
            PartyCatalogCode,
            Payload(new { display = "Alex Tenant" }),
            CancellationToken.None);

        var lease = await documents.CreateDraftAsync(
            LeaseDocumentCode,
            Payload(new { display = "Lease A-101" }),
            CancellationToken.None);

        var created = await documents.CreateDraftAsync(
            OwnerDocumentCode,
            new RecordPayload(
                Fields: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                {
                    ["display"] = JsonSerializer.SerializeToElement("Owner Ref Doc"),
                    ["party_id"] = JsonSerializer.SerializeToElement(party.Id),
                    ["lease_id"] = JsonSerializer.SerializeToElement(lease.Id),
                    ["rent_account_id"] = JsonSerializer.SerializeToElement(accountId)
                },
                Parts: new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
                {
                    ["lines"] = new RecordPartPayload(
                    [
                        new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["ordinal"] = JsonSerializer.SerializeToElement(1),
                            ["party_id"] = JsonSerializer.SerializeToElement(new RefValueDto(party.Id, "ignored")),
                            ["lease_id"] = JsonSerializer.SerializeToElement(new RefValueDto(lease.Id, "ignored")),
                            ["rent_account_id"] = JsonSerializer.SerializeToElement(new RefValueDto(accountId, "ignored"))
                        }
                    ])
                }),
            CancellationToken.None);

        var read = await documents.GetByIdAsync(OwnerDocumentCode, created.Id, CancellationToken.None);

        var headParty = ReadRef(read.Payload.Fields!["party_id"]);
        headParty.Id.Should().Be(party.Id);
        headParty.Display.Should().Be("Alex Tenant");

        var headLease = ReadRef(read.Payload.Fields!["lease_id"]);
        headLease.Id.Should().Be(lease.Id);
        headLease.Display.Should().Be("Lease A-101");

        var headAccount = ReadRef(read.Payload.Fields!["rent_account_id"]);
        headAccount.Id.Should().Be(accountId);
        headAccount.Display.Should().Be("1100 — Accounts Receivable - Tenants");

        var line = read.Payload.Parts!["lines"].Rows.Should().ContainSingle().Subject;
        ReadRef(line["party_id"]).Display.Should().Be("Alex Tenant");
        ReadRef(line["lease_id"]).Display.Should().Be("Lease A-101");
        ReadRef(line["rent_account_id"]).Display.Should().Be("1100 — Accounts Receivable - Tenants");
    }

    private static async Task EnsureTablesAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);
        await uow.Connection.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS cat_it_ref_party_doc
            (
                catalog_id uuid PRIMARY KEY,
                display text NOT NULL
            );

            CREATE TABLE IF NOT EXISTS it_ref_lease_doc
            (
                document_id uuid PRIMARY KEY,
                display text NOT NULL
            );

            CREATE TABLE IF NOT EXISTS it_doc_ref_payload
            (
                document_id uuid PRIMARY KEY,
                display text NOT NULL,
                party_id uuid NULL,
                lease_id uuid NULL,
                rent_account_id uuid NULL
            );

            CREATE TABLE IF NOT EXISTS it_doc_ref_payload__line_storage
            (
                document_id uuid NOT NULL,
                ordinal int NOT NULL,
                party_id uuid NULL,
                lease_id uuid NULL,
                rent_account_id uuid NULL,
                CONSTRAINT ux_it_doc_ref_payload__line_storage UNIQUE (document_id, ordinal)
            );
            """);
    }

    private static async Task<Guid> CreateAccountAsync(IHost host, string code, string name, AccountType type, StatementSection section)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        return await accounts.CreateAsync(new CreateAccountRequest(
            Code: code,
            Name: name,
            Type: type,
            StatementSection: section,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);
    }

    private static RecordPayload Payload(object fields)
    {
        var element = JsonSerializer.SerializeToElement(fields);
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
            dict[property.Name] = property.Value;

        return new RecordPayload(dict);
    }

    private static RefValueDto ReadRef(JsonElement element)
        => new(
            Guid.Parse(element.GetProperty("id").GetString()!),
            element.GetProperty("display").GetString()!);

    private sealed class ReferencePayloadDocumentContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddCatalog(PartyCatalogCode, c => c.Metadata(new CatalogTypeMetadata(
                CatalogCode: PartyCatalogCode,
                DisplayName: "IT Ref Party",
                Tables:
                [
                    new CatalogTableMetadata(
                        TableName: "cat_it_ref_party_doc",
                        Kind: TableKind.Head,
                        Columns:
                        [
                            new CatalogColumnMetadata("catalog_id", ColumnType.Guid, Required: true),
                            new CatalogColumnMetadata("display", ColumnType.String, Required: true)
                        ],
                        Indexes: [])
                ],
                Presentation: new CatalogPresentationMetadata("cat_it_ref_party_doc", "display"),
                Version: new CatalogMetadataVersion(1, "it-tests"))));

            builder.AddDocument(LeaseDocumentCode, d => d.Metadata(new DocumentTypeMetadata(
                TypeCode: LeaseDocumentCode,
                Tables:
                [
                    new DocumentTableMetadata(
                        TableName: "it_ref_lease_doc",
                        Kind: TableKind.Head,
                        Columns:
                        [
                            new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                            new DocumentColumnMetadata("display", ColumnType.String, Required: true)
                        ])
                ],
                Presentation: new DocumentPresentationMetadata("IT Ref Lease"),
                Version: new DocumentMetadataVersion(1, "it-tests"))));

            builder.AddDocument(OwnerDocumentCode, d => d.Metadata(new DocumentTypeMetadata(
                TypeCode: OwnerDocumentCode,
                Tables:
                [
                    new DocumentTableMetadata(
                        TableName: "it_doc_ref_payload",
                        Kind: TableKind.Head,
                        Columns:
                        [
                            new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                            new DocumentColumnMetadata("display", ColumnType.String, Required: true),
                            new DocumentColumnMetadata("party_id", ColumnType.Guid, Lookup: new CatalogLookupSourceMetadata(PartyCatalogCode)),
                            new DocumentColumnMetadata("lease_id", ColumnType.Guid, Lookup: new DocumentLookupSourceMetadata([LeaseDocumentCode])),
                            new DocumentColumnMetadata("rent_account_id", ColumnType.Guid, Lookup: new ChartOfAccountsLookupSourceMetadata())
                        ]),
                    new DocumentTableMetadata(
                        TableName: "it_doc_ref_payload__line_storage",
                        Kind: TableKind.Part,
                        PartCode: "lines",
                        Columns:
                        [
                            new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                            new DocumentColumnMetadata("ordinal", ColumnType.Int32, Required: true),
                            new DocumentColumnMetadata("party_id", ColumnType.Guid, Lookup: new CatalogLookupSourceMetadata(PartyCatalogCode)),
                            new DocumentColumnMetadata("lease_id", ColumnType.Guid, Lookup: new DocumentLookupSourceMetadata([LeaseDocumentCode])),
                            new DocumentColumnMetadata("rent_account_id", ColumnType.Guid, Lookup: new ChartOfAccountsLookupSourceMetadata())
                        ])
                ],
                Presentation: new DocumentPresentationMetadata("IT Ref Payload Owner"),
                Version: new DocumentMetadataVersion(1, "it-tests"))));
        }
    }
}
