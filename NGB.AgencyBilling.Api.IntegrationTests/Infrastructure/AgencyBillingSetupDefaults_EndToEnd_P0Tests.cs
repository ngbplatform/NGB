using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using NGB.AgencyBilling.Runtime;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using Xunit;

namespace NGB.AgencyBilling.Api.IntegrationTests.Infrastructure;

[Collection(AgencyBillingPostgresCollection.Name)]
public sealed class AgencyBillingSetupDefaults_EndToEnd_P0Tests(AgencyBillingPostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task EnsureDefaults_Is_Idempotent_And_Creates_Core_Metadata()
    {
        using var host = AgencyBillingHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<IAgencyBillingSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var first = await setup.EnsureDefaultsAsync(CancellationToken.None);
        first.CreatedCashAccount.Should().BeTrue();
        first.CreatedAccountsReceivableAccount.Should().BeTrue();
        first.CreatedServiceRevenueAccount.Should().BeTrue();
        first.CreatedProjectTimeLedgerOperationalRegister.Should().BeTrue();
        first.CreatedUnbilledTimeOperationalRegister.Should().BeTrue();
        first.CreatedProjectBillingStatusOperationalRegister.Should().BeTrue();
        first.CreatedArOpenItemsOperationalRegister.Should().BeTrue();
        first.CreatedAccountingPolicy.Should().BeTrue();

        var second = await setup.EnsureDefaultsAsync(CancellationToken.None);
        second.CreatedCashAccount.Should().BeFalse();
        second.CreatedAccountsReceivableAccount.Should().BeFalse();
        second.CreatedServiceRevenueAccount.Should().BeFalse();
        second.CreatedProjectTimeLedgerOperationalRegister.Should().BeFalse();
        second.CreatedUnbilledTimeOperationalRegister.Should().BeFalse();
        second.CreatedProjectBillingStatusOperationalRegister.Should().BeFalse();
        second.CreatedArOpenItemsOperationalRegister.Should().BeFalse();
        second.CreatedAccountingPolicy.Should().BeFalse();

        var paymentTermsPage = await catalogs.GetPageAsync(
            AgencyBillingCodes.PaymentTerms,
            new PageRequestDto(0, 50, null),
            CancellationToken.None);

        paymentTermsPage.Items.Should().Contain(x => x.Display == "Due on Receipt");
        paymentTermsPage.Items.Should().Contain(x => x.Display == "Net 15");
        paymentTermsPage.Items.Should().Contain(x => x.Display == "Net 30");

        var policyPage = await catalogs.GetPageAsync(
            AgencyBillingCodes.AccountingPolicy,
            new PageRequestDto(0, 10, null),
            CancellationToken.None);

        policyPage.Items.Should().ContainSingle();
    }
}
