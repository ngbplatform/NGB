using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.CRM.Api.IntegrationTests.Infrastructure;
using NGB.CRM.Api.IntegrationTests.Support;
using NGB.CRM.Runtime;
using NGB.Contracts.Common;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Catalogs;

[Collection(CrmPostgresCollection.Name)]
public sealed class CrmCatalogs_EndToEnd_P0Tests(CrmPostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Account_Contact_Product_Catalogs_Support_Metadata_Paging_Search_Lookup_And_Update()
    {
        using var host = CrmHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<ICrmSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var metadata = await catalogs.GetAllMetadataAsync(CancellationToken.None);
        metadata.Select(x => x.CatalogType).Should().Contain([
            CrmCodes.Account,
            CrmCodes.Contact,
            CrmCodes.Product,
            CrmCodes.OpportunityStage
        ]);

        var account = await CrmIntegrationTestHelpers.CreateCatalogAsync(catalogs, CrmCodes.Account, new
        {
            display = "Catalog Test Account",
            account_number = "CRM-CAT-100",
            name = "Catalog Test Account",
            account_type = "Prospect",
            industry = "Technology",
            is_active = true
        });

        var contact = await CrmIntegrationTestHelpers.CreateCatalogAsync(catalogs, CrmCodes.Contact, new
        {
            display = "Catalog Test Contact",
            account_id = account.Id,
            first_name = "Casey",
            last_name = "Morgan",
            email = "casey.morgan@catalog.example",
            is_primary = true,
            is_active = true
        });

        var page = await catalogs.GetPageAsync(
            CrmCodes.Account,
            new PageRequestDto(Offset: 0, Limit: 10, Search: "Catalog Test"),
            CancellationToken.None);
        page.Items.Should().ContainSingle(x => x.Id == account.Id);

        var lookup = await catalogs.LookupAsync(CrmCodes.Contact, "Catalog Test", 10, CancellationToken.None);
        lookup.Should().ContainSingle(x => x.Id == contact.Id && x.Label == "Catalog Test Contact");

        var byIds = await catalogs.GetByIdsAsync(CrmCodes.Account, [account.Id], CancellationToken.None);
        byIds.Should().ContainSingle(x => x.Id == account.Id && x.Label == "Catalog Test Account");

        var updated = await catalogs.UpdateAsync(CrmCodes.Account, account.Id, CrmIntegrationTestHelpers.Payload(new
        {
            display = "Catalog Test Account",
            account_number = "CRM-CAT-100",
            name = "Catalog Test Account",
            account_type = "Customer",
            industry = "Technology",
            website = "https://catalog-test.example",
            is_active = true
        }), CancellationToken.None);

        updated.Payload.Fields.Should().NotBeNull();
        updated.Payload.Fields!["account_type"].ToString().Should().Be("Customer");
        updated.Payload.Fields!["website"].ToString().Should().Be("https://catalog-test.example");
    }
}
