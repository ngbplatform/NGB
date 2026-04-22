using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Reports;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReporting_DefinitionEnrichment_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmReporting_DefinitionEnrichment_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Pm_Host_Enriches_Shared_Accounting_Definitions_With_Pm_Filters_And_ReportSpecific_Filter_Shape()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using (var resp = await client.GetAsync("/api/report-definitions/accounting.general_journal"))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var def = await resp.Content.ReadFromJsonAsync<ReportDefinitionDto>(Json);
            def.Should().NotBeNull();
            var filters = def!.Filters!;
            filters.Select(x => x.FieldCode)
                .Should().NotContain("document_id")
                .And.Contain("property_id").And.Contain("lease_id").And.Contain("party_id");

            def.Description.Should().Be("Transaction Log");
            def.Capabilities!.AllowsGrandTotals.Should().BeFalse();
            def.DefaultLayout!.ShowGrandTotals.Should().BeFalse();

            var stornoFilter = filters.Single(x => x.FieldCode == "is_storno");
            stornoFilter.Options!.Select(x => new { x.Value, x.Label })
                .Should().Equal(
                    new { Value = "True", Label = "Yes" },
                    new { Value = "False", Label = "No" });
        }

        using (var resp = await client.GetAsync("/api/report-definitions/accounting.ledger.analysis"))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var def = await resp.Content.ReadFromJsonAsync<ReportDefinitionDto>(Json);
            def.Should().NotBeNull();
            var filters = def!.Filters!;
            filters.Select(x => x.FieldCode)
                .Should().Equal("account_id", "property_id", "lease_id", "party_id");

            var propertyFilter = filters.Single(x => x.FieldCode == "property_id");
            propertyFilter.IsMulti.Should().BeTrue();
            propertyFilter.SupportsIncludeDescendants.Should().BeTrue();
            propertyFilter.DefaultIncludeDescendants.Should().BeTrue();
            propertyFilter.Lookup.Should().BeOfType<CatalogLookupSourceDto>();
            var propertyLookup = (CatalogLookupSourceDto)propertyFilter.Lookup!;
            propertyLookup.CatalogType.Should().Be("pm.property");

            def.Dataset.Should().NotBeNull();
            def.Dataset!.DatasetCode.Should().Be("pm.accounting.ledger.analysis");
            def.Dataset.Fields!.Where(x => x.Code is "property_id" or "lease_id" or "party_id")
                .Select(x => new
                {
                    x.Code,
                    x.IsFilterable,
                    x.IsGroupable,
                    x.IsSelectable,
                    x.IsSortable,
                    x.SupportsIncludeDescendants,
                    x.DefaultIncludeDescendants
                })
                .Should().BeEquivalentTo(
                [
                    new
                    {
                        Code = "property_id",
                        IsFilterable = true,
                        IsGroupable = false,
                        IsSelectable = false,
                        IsSortable = false,
                        SupportsIncludeDescendants = true,
                        DefaultIncludeDescendants = true
                    },
                    new
                    {
                        Code = "lease_id",
                        IsFilterable = true,
                        IsGroupable = false,
                        IsSelectable = false,
                        IsSortable = false,
                        SupportsIncludeDescendants = false,
                        DefaultIncludeDescendants = false
                    },
                    new
                    {
                        Code = "party_id",
                        IsFilterable = true,
                        IsGroupable = false,
                        IsSelectable = false,
                        IsSortable = false,
                        SupportsIncludeDescendants = false,
                        DefaultIncludeDescendants = false
                    }
                ]);
            def.Dataset!.Fields!.Where(x => x.IsGroupable == true).Select(x => x.Label)
                .Should().Equal("Period", "Account", "Document");
            def.Dataset.Fields!.Where(x => x.IsSelectable == true).Select(x => x.Label)
                .Should().Equal("Period", "Account", "Document");
            def.Dataset.Fields!.Where(x => x.IsSortable == true).Select(x => x.Label)
                .Should().Equal("Period", "Account", "Document");
            def.Dataset.Measures!.Select(x => x.Label).Should().Equal("Debit", "Credit", "Net");
        }
    }

    [Fact]
    public async Task Pm_Canonical_Receivables_Reports_Expose_Lease_As_The_Only_Scope_Filter()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        foreach (var reportCode in new[]
                 {
                     "pm.tenant.statement",
                     "pm.receivables.aging",
                     "pm.receivables.open_items",
                     "pm.receivables.open_items.details"
                 })
        {
            using var resp = await client.GetAsync($"/api/report-definitions/{reportCode}");
            resp.StatusCode.Should().Be(HttpStatusCode.OK);

            var def = await resp.Content.ReadFromJsonAsync<ReportDefinitionDto>(Json);
            def.Should().NotBeNull();
            def!.Filters!.Select(x => new { x.FieldCode, x.Label, x.IsRequired })
                .Should().Equal(new { FieldCode = "lease_id", Label = "Lease", IsRequired = true });
        }
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
