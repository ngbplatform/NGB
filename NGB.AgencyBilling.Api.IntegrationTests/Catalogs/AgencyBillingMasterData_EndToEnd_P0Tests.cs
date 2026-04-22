using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.AgencyBilling.Api.IntegrationTests.Infrastructure;
using NGB.AgencyBilling.Enums;
using NGB.AgencyBilling.Runtime;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using Xunit;

namespace NGB.AgencyBilling.Api.IntegrationTests.Catalogs;

[Collection(AgencyBillingPostgresCollection.Name)]
public sealed class AgencyBillingMasterData_EndToEnd_P0Tests(AgencyBillingPostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Core_Master_Data_Catalogs_Can_Be_Created_And_Listed()
    {
        using var host = AgencyBillingHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<IAgencyBillingSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var client = await catalogs.CreateAsync(
            AgencyBillingCodes.Client,
            Payload(new
            {
                display = "Northwind Creative",
                client_code = "CLI-100",
                name = "Northwind Creative",
                legal_name = "Northwind Creative LLC",
                status = (int)AgencyBillingClientStatus.Active,
                email = "ap@northwindcreative.example",
                is_active = true,
                default_currency = AgencyBillingCodes.DefaultCurrency
            }),
            CancellationToken.None);

        var teamMember = await catalogs.CreateAsync(
            AgencyBillingCodes.TeamMember,
            Payload(new
            {
                display = "Ava Stone",
                member_code = "TM-100",
                full_name = "Ava Stone",
                member_type = (int)AgencyBillingTeamMemberType.Employee,
                is_active = true,
                billable_by_default = true,
                default_billing_rate = 180m,
                default_cost_rate = 70m,
                email = "ava.stone@example.test",
                title = "Senior Consultant"
            }),
            CancellationToken.None);

        var serviceItem = await catalogs.CreateAsync(
            AgencyBillingCodes.ServiceItem,
            Payload(new
            {
                display = "Strategy",
                code = "STRATEGY",
                name = "Strategy",
                unit_of_measure = (int)AgencyBillingServiceItemUnitOfMeasure.Hour,
                is_active = true
            }),
            CancellationToken.None);

        var project = await catalogs.CreateAsync(
            AgencyBillingCodes.Project,
            Payload(new
            {
                display = "Northwind Revamp",
                project_code = "PRJ-100",
                name = "Northwind Revamp",
                client_id = client.Id,
                project_manager_id = teamMember.Id,
                status = (int)AgencyBillingProjectStatus.Active,
                billing_model = (int)AgencyBillingProjectBillingModel.TimeAndMaterials,
                budget_hours = 250m,
                budget_amount = 45000m
            }),
            CancellationToken.None);

        var rateCard = await catalogs.CreateAsync(
            AgencyBillingCodes.RateCard,
            Payload(new
            {
                display = "Northwind Strategy Rate",
                name = "Northwind Strategy Rate",
                client_id = client.Id,
                project_id = project.Id,
                team_member_id = teamMember.Id,
                service_item_id = serviceItem.Id,
                service_title = "Strategy",
                billing_rate = 180m,
                cost_rate = 70m,
                is_active = true
            }),
            CancellationToken.None);

        client.Display.Should().Be("Northwind Creative");
        teamMember.Display.Should().Be("Ava Stone");
        project.Display.Should().Be("Northwind Revamp");
        rateCard.Display.Should().Be("Northwind Strategy Rate");

        var clientsPage = await catalogs.GetPageAsync(AgencyBillingCodes.Client, new PageRequestDto(0, 25, "Northwind"), CancellationToken.None);
        var projectsPage = await catalogs.GetPageAsync(AgencyBillingCodes.Project, new PageRequestDto(0, 25, "Revamp"), CancellationToken.None);

        clientsPage.Items.Should().ContainSingle(x => x.Id == client.Id);
        projectsPage.Items.Should().ContainSingle(x => x.Id == project.Id);
    }

    [Fact]
    public async Task Catalog_And_Document_Metadata_Expose_Numeric_Enum_Options()
    {
        using var host = AgencyBillingHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var clientMetadata = await catalogs.GetTypeMetadataAsync(AgencyBillingCodes.Client, CancellationToken.None);
        var teamMemberMetadata = await catalogs.GetTypeMetadataAsync(AgencyBillingCodes.TeamMember, CancellationToken.None);
        var projectMetadata = await catalogs.GetTypeMetadataAsync(AgencyBillingCodes.Project, CancellationToken.None);
        var serviceItemMetadata = await catalogs.GetTypeMetadataAsync(AgencyBillingCodes.ServiceItem, CancellationToken.None);
        var contractMetadata = await documents.GetTypeMetadataAsync(AgencyBillingCodes.ClientContract, CancellationToken.None);

        FieldMetadataDto FindField(FormMetadataDto? form, string key)
            => form!.Sections
                .SelectMany(section => section.Rows)
                .SelectMany(row => row.Fields)
                .Single(field => field.Key == key);

        var clientStatus = FindField(clientMetadata.Form, "status");
        clientStatus.DataType.Should().Be(DataType.Int32);
        clientStatus.Options.Should().ContainEquivalentOf(new MetadataOptionDto("1", "Active"));
        clientStatus.Options.Should().ContainEquivalentOf(new MetadataOptionDto("2", "On Hold"));
        clientStatus.Options.Should().ContainEquivalentOf(new MetadataOptionDto("3", "Inactive"));

        var memberType = FindField(teamMemberMetadata.Form, "member_type");
        memberType.DataType.Should().Be(DataType.Int32);
        memberType.Options.Should().ContainEquivalentOf(new MetadataOptionDto("1", "Employee"));
        memberType.Options.Should().ContainEquivalentOf(new MetadataOptionDto("2", "Contractor"));

        var projectStatus = FindField(projectMetadata.Form, "status");
        projectStatus.DataType.Should().Be(DataType.Int32);
        projectStatus.Options.Should().ContainEquivalentOf(new MetadataOptionDto("4", "On Hold"));

        var billingModel = FindField(projectMetadata.Form, "billing_model");
        billingModel.DataType.Should().Be(DataType.Int32);
        billingModel.Options.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new MetadataOptionDto("1", "Time & Materials"));

        var unitOfMeasure = FindField(serviceItemMetadata.Form, "unit_of_measure");
        unitOfMeasure.DataType.Should().Be(DataType.Int32);
        unitOfMeasure.Options.Should().ContainEquivalentOf(new MetadataOptionDto("1", "Hour"));

        var billingFrequency = FindField(contractMetadata.Form, "billing_frequency");
        billingFrequency.DataType.Should().Be(DataType.Int32);
        billingFrequency.Options.Should().ContainEquivalentOf(new MetadataOptionDto("4", "Monthly"));
    }

    private static RecordPayload Payload(object value)
        => new(
            JsonSerializer.SerializeToElement(value).EnumerateObject().ToDictionary(
                static x => x.Name,
                static x => x.Value.Clone(),
                StringComparer.OrdinalIgnoreCase),
            null);
}
