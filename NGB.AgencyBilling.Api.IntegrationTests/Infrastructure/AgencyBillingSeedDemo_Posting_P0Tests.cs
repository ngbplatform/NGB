using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.AgencyBilling.Migrator.Seed;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using Xunit;

namespace NGB.AgencyBilling.Api.IntegrationTests.Infrastructure;

[Collection(AgencyBillingPostgresCollection.Name)]
public sealed class AgencyBillingSeedDemo_Posting_P0Tests(AgencyBillingPostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SeedDemo_Posts_Core_Documents_When_Posting_Is_Configured()
    {
        var exitCode = await AgencyBillingSeedDemoCli.RunAsync(
        [
            "--connection", fixture.ConnectionString,
            "--seed", "20260417",
            "--from", "2026-01-01",
            "--to", "2026-01-31",
            "--clients", "2",
            "--team-members", "3",
            "--projects", "2",
            "--timesheets", "5",
            "--sales-invoices", "2",
            "--customer-payments", "2"
        ]);

        exitCode.Should().Be(0);

        using var host = AgencyBillingHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        (await GetDocumentCountAsync(documents, AgencyBillingCodes.ClientContract)).Should().Be(2);
        (await GetDocumentCountAsync(documents, AgencyBillingCodes.Timesheet)).Should().Be(5);
        (await GetDocumentCountAsync(documents, AgencyBillingCodes.SalesInvoice)).Should().Be(2);
        (await GetDocumentCountAsync(documents, AgencyBillingCodes.CustomerPayment)).Should().Be(2);

        (await GetFirstStatusAsync(documents, AgencyBillingCodes.ClientContract)).Should().Be(DocumentStatus.Posted);
        (await GetFirstStatusAsync(documents, AgencyBillingCodes.Timesheet)).Should().Be(DocumentStatus.Posted);
        (await GetFirstStatusAsync(documents, AgencyBillingCodes.SalesInvoice)).Should().Be(DocumentStatus.Posted);
        (await GetFirstStatusAsync(documents, AgencyBillingCodes.CustomerPayment)).Should().Be(DocumentStatus.Posted);

        (await GetFirstDisplayAsync(documents, AgencyBillingCodes.ClientContract)).Should().StartWith("Client Contract ");
        (await GetFirstDisplayAsync(documents, AgencyBillingCodes.Timesheet)).Should().StartWith("Timesheet ");
        (await GetFirstDisplayAsync(documents, AgencyBillingCodes.SalesInvoice)).Should().StartWith("Sales Invoice ");
        (await GetFirstDisplayAsync(documents, AgencyBillingCodes.CustomerPayment)).Should().StartWith("Customer Payment ");
    }

    private static async Task<int> GetDocumentCountAsync(IDocumentService documents, string documentType)
    {
        var page = await documents.GetPageAsync(documentType, new PageRequestDto(0, 1, null), CancellationToken.None);
        return page.Total.GetValueOrDefault(page.Items.Count);
    }

    private static async Task<DocumentStatus> GetFirstStatusAsync(IDocumentService documents, string documentType)
    {
        var page = await documents.GetPageAsync(documentType, new PageRequestDto(0, 1, null), CancellationToken.None);
        return page.Items.Single().Status;
    }

    private static async Task<string> GetFirstDisplayAsync(IDocumentService documents, string documentType)
    {
        var page = await documents.GetPageAsync(documentType, new PageRequestDto(0, 1, null), CancellationToken.None);
        var display = page.Items.Single().Display;
        display.Should().NotBeNullOrWhiteSpace();
        return display!;
    }
}
