using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.Contracts.Metadata;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Catalogs;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmMetadata_FieldOverrides_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmMetadata_FieldOverrides_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task BankAccount_Catalog_Metadata_Uses_Explicit_Field_Labels_And_ReadOnly_Computed_Display()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var resp = await client.GetAsync($"/api/catalogs/{PropertyManagementCodes.BankAccount}/metadata");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var meta = await resp.Content.ReadFromJsonAsync<CatalogTypeMetadataDto>(Json);
        meta.Should().NotBeNull();
        meta!.CatalogType.Should().Be(PropertyManagementCodes.BankAccount);

        meta.List!.Columns.Should().Contain(c => c.Key == "last4" && c.Label == "Last 4 digits");
        meta.List!.Columns.Should().Contain(c => c.Key == "gl_account_id" && c.Label == "GL Account");

        var fields = meta.Form!.Sections.SelectMany(s => s.Rows).SelectMany(r => r.Fields).ToList();
        fields.Should().Contain(f => f.Key == "display" && f.IsReadOnly);
        fields.Should().Contain(f => f.Key == "last4" && f.Label == "Last 4 digits");
        fields.Should().Contain(f => f.Key == "gl_account_id" && f.Label == "GL Account");
    }

    [Fact]
    public async Task Property_Catalog_Metadata_Marks_Computed_Display_As_ReadOnly()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var resp = await client.GetAsync($"/api/catalogs/{PropertyManagementCodes.Property}/metadata");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var meta = await resp.Content.ReadFromJsonAsync<CatalogTypeMetadataDto>(Json);
        meta.Should().NotBeNull();

        var fields = meta!.Form!.Sections.SelectMany(s => s.Rows).SelectMany(r => r.Fields).ToList();
        fields.Should().Contain(f => f.Key == "display" && f.IsReadOnly);
    }

    [Fact]
    public async Task Payment_Document_Metadata_Uses_BankAccount_Label_And_Explicit_Lookup_Source()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var paymentResp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.ReceivablePayment}/metadata");
        paymentResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var paymentMeta = await paymentResp.Content.ReadFromJsonAsync<DocumentTypeMetadataDto>(Json);
        paymentMeta.Should().NotBeNull();

        var paymentField = paymentMeta!.Form!.Sections
            .SelectMany(s => s.Rows)
            .SelectMany(r => r.Fields)
            .Single(f => f.Key == "bank_account_id");

        paymentField.Label.Should().Be("Bank Account");
        paymentField.Lookup.Should().NotBeNull();
        paymentField.Lookup.ShouldBeCatalogLookup(PropertyManagementCodes.BankAccount);

        using var returnedResp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.ReceivableReturnedPayment}/metadata");
        returnedResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var returnedMeta = await returnedResp.Content.ReadFromJsonAsync<DocumentTypeMetadataDto>(Json);
        returnedMeta.Should().NotBeNull();

        var returnedFields = returnedMeta!.Form!.Sections
            .SelectMany(s => s.Rows)
            .SelectMany(r => r.Fields)
            .ToList();

        returnedFields.Should().NotContain(f => f.Key == "bank_account_id");
        returnedFields.Should().NotContain(f => f.Key == "party_id");
        returnedFields.Should().NotContain(f => f.Key == "property_id");
        returnedFields.Should().NotContain(f => f.Key == "lease_id");

        var originalPaymentField = returnedFields.Single(f => f.Key == "original_payment_id");
        originalPaymentField.Lookup.ShouldBeDocumentLookup(PropertyManagementCodes.ReceivablePayment);
    }

    [Fact]
    public async Task AccountingPolicy_Catalog_Metadata_Uses_Explicit_AR_Label()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var resp = await client.GetAsync($"/api/catalogs/{PropertyManagementCodes.AccountingPolicy}/metadata");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var meta = await resp.Content.ReadFromJsonAsync<CatalogTypeMetadataDto>(Json);
        meta.Should().NotBeNull();

        var fields = meta!.Form!.Sections.SelectMany(s => s.Rows).SelectMany(r => r.Fields).ToList();
        fields.Should().Contain(f => f.Key == "ar_tenants_account_id" && f.Label == "AR Tenants Account");
        fields.Should().Contain(f => f.Key == "ap_vendors_account_id" && f.Label == "AP Vendors Account");
    }

    [Fact]
    public async Task PayableCharge_Document_Metadata_Uses_Vendor_Label_And_Explicit_Lookups()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var resp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.PayableCharge}/metadata");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var meta = await resp.Content.ReadFromJsonAsync<DocumentTypeMetadataDto>(Json);
        meta.Should().NotBeNull();

        var fields = meta!.Form!.Sections.SelectMany(s => s.Rows).SelectMany(r => r.Fields).ToList();
        fields.Single(f => f.Key == "party_id").Lookup.ShouldBeCatalogLookup(PropertyManagementCodes.Party);
        fields.Single(f => f.Key == "party_id").Label.Should().Be("Vendor");
        fields.Single(f => f.Key == "charge_type_id").Lookup.ShouldBeCatalogLookup(PropertyManagementCodes.PayableChargeType);
        fields.Single(f => f.Key == "charge_type_id").Label.Should().Be("Charge Type");
        fields.Should().Contain(f => f.Key == "vendor_invoice_no" && f.Label == "Vendor Invoice No");
    }

    [Fact]
    public async Task PayablePayment_Document_Metadata_Uses_Vendor_And_BankAccount_Labels()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var resp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.PayablePayment}/metadata");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var meta = await resp.Content.ReadFromJsonAsync<DocumentTypeMetadataDto>(Json);
        meta.Should().NotBeNull();

        var fields = meta!.Form!.Sections.SelectMany(s => s.Rows).SelectMany(r => r.Fields).ToList();
        fields.Single(f => f.Key == "party_id").Lookup.ShouldBeCatalogLookup(PropertyManagementCodes.Party);
        fields.Single(f => f.Key == "party_id").Label.Should().Be("Vendor");
        fields.Single(f => f.Key == "bank_account_id").Lookup.ShouldBeCatalogLookup(PropertyManagementCodes.BankAccount);
        fields.Single(f => f.Key == "bank_account_id").Label.Should().Be("Bank Account");
        fields.Should().NotContain(f => f.Key == "original_charge_id");
        fields.Should().Contain(f => f.Key == "paid_on_utc" && f.Label == "Paid On");
    }

    [Fact]
    public async Task ReceivableApply_Document_Metadata_Uses_CreditSource_And_Charge_Labels()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var resp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.ReceivableApply}/metadata");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var meta = await resp.Content.ReadFromJsonAsync<DocumentTypeMetadataDto>(Json);
        meta.Should().NotBeNull();

        var fields = meta!.Form!.Sections.SelectMany(s => s.Rows).SelectMany(r => r.Fields).ToList();
        fields.Should().Contain(f => f.Key == "credit_document_id" && f.Label == "Credit Source");
        fields.Should().Contain(f => f.Key == "charge_document_id" && f.Label == "Charge");
        fields.Should().Contain(f => f.Key == "applied_on_utc" && f.Label == "Applied On");
    }

    [Fact]
    public async Task PayableApply_Document_Metadata_Uses_CreditSource_And_Charge_Labels()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var resp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.PayableApply}/metadata");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var meta = await resp.Content.ReadFromJsonAsync<DocumentTypeMetadataDto>(Json);
        meta.Should().NotBeNull();

        var fields = meta!.Form!.Sections.SelectMany(s => s.Rows).SelectMany(r => r.Fields).ToList();
        fields.Should().Contain(f => f.Key == "credit_document_id" && f.Label == "Credit Source");
        fields.Should().Contain(f => f.Key == "charge_document_id" && f.Label == "Charge");
        fields.Should().Contain(f => f.Key == "applied_on_utc" && f.Label == "Applied On");
    }

    [Fact]
    public async Task Explicit_Lookup_Metadata_Uses_Typed_Sources_For_Document_And_Coa_Fields()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var bankResp = await client.GetAsync($"/api/catalogs/{PropertyManagementCodes.BankAccount}/metadata");
        bankResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var bankMeta = await bankResp.Content.ReadFromJsonAsync<CatalogTypeMetadataDto>(Json);
        bankMeta.Should().NotBeNull();

        var glField = bankMeta!.Form!.Sections.SelectMany(s => s.Rows).SelectMany(r => r.Fields).Single(f => f.Key == "gl_account_id");
        glField.Lookup.ShouldBeCoaLookup();

        using var receivableApplyResp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.ReceivableApply}/metadata");
        receivableApplyResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var receivableApplyMeta = await receivableApplyResp.Content.ReadFromJsonAsync<DocumentTypeMetadataDto>(Json);
        receivableApplyMeta.Should().NotBeNull();

        var receivableApplyFields = receivableApplyMeta!.Form!.Sections.SelectMany(s => s.Rows).SelectMany(r => r.Fields).ToList();
        receivableApplyFields.Single(f => f.Key == "credit_document_id").Lookup.ShouldBeDocumentLookup(PropertyManagementCodes.ReceivablePayment, PropertyManagementCodes.ReceivableCreditMemo);
        receivableApplyFields.Single(f => f.Key == "charge_document_id").Lookup.ShouldBeDocumentLookup(PropertyManagementCodes.ReceivableCharge, PropertyManagementCodes.LateFeeCharge, PropertyManagementCodes.RentCharge);
        receivableApplyMeta.List!.Columns.Single(c => c.Key == "credit_document_id").Lookup.ShouldBeDocumentLookup(PropertyManagementCodes.ReceivablePayment, PropertyManagementCodes.ReceivableCreditMemo);

        using var payableApplyResp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.PayableApply}/metadata");
        payableApplyResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var payableApplyMeta = await payableApplyResp.Content.ReadFromJsonAsync<DocumentTypeMetadataDto>(Json);
        payableApplyMeta.Should().NotBeNull();

        var payableApplyFields = payableApplyMeta!.Form!.Sections.SelectMany(s => s.Rows).SelectMany(r => r.Fields).ToList();
        payableApplyFields.Single(f => f.Key == "credit_document_id").Lookup.ShouldBeDocumentLookup(PropertyManagementCodes.PayablePayment, PropertyManagementCodes.PayableCreditMemo);
        payableApplyFields.Single(f => f.Key == "charge_document_id").Lookup.ShouldBeDocumentLookup(PropertyManagementCodes.PayableCharge);
        payableApplyMeta.List!.Columns.Single(c => c.Key == "credit_document_id").Lookup.ShouldBeDocumentLookup(PropertyManagementCodes.PayablePayment, PropertyManagementCodes.PayableCreditMemo);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

internal static class LookupSourceAssertions
{
    public static void ShouldBeCatalogLookup(this LookupSourceDto? lookup, string expectedCatalogType)
    {
        lookup.Should().BeOfType<CatalogLookupSourceDto>();
        ((CatalogLookupSourceDto)lookup!).CatalogType.Should().Be(expectedCatalogType);
    }

    public static void ShouldBeDocumentLookup(this LookupSourceDto? lookup, params string[] expectedDocumentTypes)
    {
        lookup.Should().BeOfType<DocumentLookupSourceDto>();
        ((DocumentLookupSourceDto)lookup!).DocumentTypes.Should().BeEquivalentTo(expectedDocumentTypes, options => options.WithStrictOrdering());
    }

    public static void ShouldBeCoaLookup(this LookupSourceDto? lookup)
    {
        lookup.Should().BeOfType<ChartOfAccountsLookupSourceDto>();
    }
}
