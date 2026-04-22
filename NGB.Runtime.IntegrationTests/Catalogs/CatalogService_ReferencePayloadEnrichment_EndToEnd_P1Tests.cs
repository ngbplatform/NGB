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

namespace NGB.Runtime.IntegrationTests.Catalogs;

[Collection(PostgresCollection.Name)]
public sealed class CatalogService_ReferencePayloadEnrichment_EndToEnd_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string PartyCatalogCode = "it_ref_party_cat";
    private const string LeaseDocumentCode = "it_ref_lease_cat";
    private const string OwnerCatalogCode = "it_catalog_ref_payload";

    [Fact]
    public async Task GetByIdAsync_EnrichesCatalogReferenceFields_AndFallsBackToGuidWhenDisplayCannotBeResolved()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, ReferencePayloadCatalogContributor>());

        await EnsureTablesAsync(host);
        var accountId = await CreateAccountAsync(host, code: "1200", name: "Other Receivables", AccountType.Asset, StatementSection.Assets);

        await using var scope = host.Services.CreateAsyncScope();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var party = await catalogs.CreateAsync(
            PartyCatalogCode,
            Payload(new { display = "Jamie Tenant" }),
            CancellationToken.None);

        var lease = await documents.CreateDraftAsync(
            LeaseDocumentCode,
            Payload(new { display = "Lease B-202" }),
            CancellationToken.None);

        var missingLeaseId = Guid.CreateVersion7();

        var created = await catalogs.CreateAsync(
            OwnerCatalogCode,
            new RecordPayload(
                Fields: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                {
                    ["display"] = JsonSerializer.SerializeToElement("Owner Catalog Ref"),
                    ["party_id"] = JsonSerializer.SerializeToElement(party.Id),
                    ["lease_id"] = JsonSerializer.SerializeToElement(lease.Id),
                    ["rent_account_id"] = JsonSerializer.SerializeToElement(accountId),
                    ["missing_lease_id"] = JsonSerializer.SerializeToElement(missingLeaseId)
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

        var read = await catalogs.GetByIdAsync(OwnerCatalogCode, created.Id, CancellationToken.None);

        ReadRef(read.Payload.Fields!["party_id"]).Display.Should().Be("Jamie Tenant");
        ReadRef(read.Payload.Fields!["lease_id"]).Display.Should().Be("Lease B-202");
        ReadRef(read.Payload.Fields!["rent_account_id"]).Display.Should().Be("1200 — Other Receivables");

        var fallbackLease = ReadRef(read.Payload.Fields!["missing_lease_id"]);
        fallbackLease.Id.Should().Be(missingLeaseId);
        fallbackLease.Display.Should().Be(missingLeaseId.ToString(), "unresolved references should degrade to GUID text instead of failing the whole payload enrichment path");

        var line = read.Payload.Parts!["lines"].Rows.Should().ContainSingle().Subject;
        ReadRef(line["party_id"]).Display.Should().Be("Jamie Tenant");
        ReadRef(line["lease_id"]).Display.Should().Be("Lease B-202");
        ReadRef(line["rent_account_id"]).Display.Should().Be("1200 — Other Receivables");
    }

    private static async Task EnsureTablesAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);
        await uow.Connection.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS cat_it_ref_party_cat
            (
                catalog_id uuid PRIMARY KEY,
                display text NOT NULL
            );

            CREATE TABLE IF NOT EXISTS it_ref_lease_cat
            (
                document_id uuid PRIMARY KEY,
                display text NOT NULL
            );

            CREATE TABLE IF NOT EXISTS cat_it_catalog_ref_payload
            (
                catalog_id uuid PRIMARY KEY,
                display text NOT NULL,
                party_id uuid NULL,
                lease_id uuid NULL,
                rent_account_id uuid NULL,
                missing_lease_id uuid NULL
            );

            CREATE TABLE IF NOT EXISTS cat_it_catalog_ref_payload__line_storage
            (
                catalog_id uuid NOT NULL,
                ordinal int NOT NULL,
                party_id uuid NULL,
                lease_id uuid NULL,
                rent_account_id uuid NULL,
                CONSTRAINT ux_cat_it_catalog_ref_payload__line_storage UNIQUE (catalog_id, ordinal)
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

    private sealed class ReferencePayloadCatalogContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddCatalog(PartyCatalogCode, c => c.Metadata(new CatalogTypeMetadata(
                CatalogCode: PartyCatalogCode,
                DisplayName: "IT Ref Party",
                Tables:
                [
                    new CatalogTableMetadata(
                        TableName: "cat_it_ref_party_cat",
                        Kind: TableKind.Head,
                        Columns:
                        [
                            new CatalogColumnMetadata("catalog_id", ColumnType.Guid, Required: true),
                            new CatalogColumnMetadata("display", ColumnType.String, Required: true)
                        ],
                        Indexes: [])
                ],
                Presentation: new CatalogPresentationMetadata("cat_it_ref_party_cat", "display"),
                Version: new CatalogMetadataVersion(1, "it-tests"))));

            builder.AddDocument(LeaseDocumentCode, d => d.Metadata(new DocumentTypeMetadata(
                TypeCode: LeaseDocumentCode,
                Tables:
                [
                    new DocumentTableMetadata(
                        TableName: "it_ref_lease_cat",
                        Kind: TableKind.Head,
                        Columns:
                        [
                            new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                            new DocumentColumnMetadata("display", ColumnType.String, Required: true)
                        ])
                ],
                Presentation: new DocumentPresentationMetadata("IT Ref Lease"),
                Version: new DocumentMetadataVersion(1, "it-tests"))));

            builder.AddCatalog(OwnerCatalogCode, c => c.Metadata(new CatalogTypeMetadata(
                CatalogCode: OwnerCatalogCode,
                DisplayName: "IT Ref Payload Catalog",
                Tables:
                [
                    new CatalogTableMetadata(
                        TableName: "cat_it_catalog_ref_payload",
                        Kind: TableKind.Head,
                        Columns:
                        [
                            new CatalogColumnMetadata("catalog_id", ColumnType.Guid, Required: true),
                            new CatalogColumnMetadata("display", ColumnType.String, Required: true),
                            new CatalogColumnMetadata("party_id", ColumnType.Guid, Lookup: new CatalogLookupSourceMetadata(PartyCatalogCode)),
                            new CatalogColumnMetadata("lease_id", ColumnType.Guid, Lookup: new DocumentLookupSourceMetadata([LeaseDocumentCode])),
                            new CatalogColumnMetadata("rent_account_id", ColumnType.Guid, Lookup: new ChartOfAccountsLookupSourceMetadata()),
                            new CatalogColumnMetadata("missing_lease_id", ColumnType.Guid, Lookup: new DocumentLookupSourceMetadata([LeaseDocumentCode]))
                        ],
                        Indexes: []),
                    new CatalogTableMetadata(
                        TableName: "cat_it_catalog_ref_payload__line_storage",
                        Kind: TableKind.Part,
                        PartCode: "lines",
                        Columns:
                        [
                            new CatalogColumnMetadata("catalog_id", ColumnType.Guid, Required: true),
                            new CatalogColumnMetadata("ordinal", ColumnType.Int32, Required: true),
                            new CatalogColumnMetadata("party_id", ColumnType.Guid, Lookup: new CatalogLookupSourceMetadata(PartyCatalogCode)),
                            new CatalogColumnMetadata("lease_id", ColumnType.Guid, Lookup: new DocumentLookupSourceMetadata([LeaseDocumentCode])),
                            new CatalogColumnMetadata("rent_account_id", ColumnType.Guid, Lookup: new ChartOfAccountsLookupSourceMetadata())
                        ],
                        Indexes: [])
                ],
                Presentation: new CatalogPresentationMetadata("cat_it_catalog_ref_payload", "display"),
                Version: new CatalogMetadataVersion(1, "it-tests"))));
        }
    }
}
