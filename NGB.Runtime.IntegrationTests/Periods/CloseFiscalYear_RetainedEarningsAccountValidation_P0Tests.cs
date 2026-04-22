using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseFiscalYear_RetainedEarningsAccountValidation_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly EndPeriod = new(2026, 1, 1);

    [Fact]
    public async Task CloseFiscalYear_WhenRetainedEarningsNotFound_Throws()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        Func<Task> act = () => svc.CloseFiscalYearAsync(
            fiscalYearEndPeriod: EndPeriod,
            retainedEarningsAccountId: Guid.CreateVersion7(),
            closedBy: "it",
            ct: CancellationToken.None);

        (await act.Should().ThrowAsync<NgbArgumentInvalidException>())
            .Which.Message.Should().Contain("Retained earnings account not found");
    }

    [Fact]
    public async Task CloseFiscalYear_WhenRetainedEarningsInactive_IsTreatedAsNotFound_Throws()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var retainedAccountId = await CreateAccountAsync(host,
            code: "300",
            name: "Retained Earnings (Inactive)",
            type: AccountType.Equity,
            section: StatementSection.Equity,
            isContra: false,
            isActive: false);

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        Func<Task> act = () => svc.CloseFiscalYearAsync(
            EndPeriod,
            retainedAccountId,
            closedBy: "it",
            ct: CancellationToken.None);

        (await act.Should().ThrowAsync<NgbArgumentInvalidException>())
            .Which.Message.Should().Contain("Retained earnings account not found");
    }

    [Fact]
    public async Task CloseFiscalYear_WhenRetainedEarningsSoftDeleted_IsTreatedAsNotFound_Throws()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var retainedAccountId = await CreateAccountAsync(host,
            code: "301",
            name: "Retained Earnings (Deleted)",
            type: AccountType.Equity,
            section: StatementSection.Equity,
            isContra: false,
            isActive: true);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            await mgmt.MarkForDeletionAsync(retainedAccountId, CancellationToken.None);
        }

        await using var scope2 = host.Services.CreateAsyncScope();
        var svc = scope2.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        Func<Task> act = () => svc.CloseFiscalYearAsync(
            EndPeriod,
            retainedAccountId,
            closedBy: "it",
            ct: CancellationToken.None);

        (await act.Should().ThrowAsync<NgbArgumentInvalidException>())
            .Which.Message.Should().Contain("Retained earnings account not found");
    }

    [Fact]
    public async Task CloseFiscalYear_WhenRetainedEarningsNotEquitySection_Throws()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var retainedAccountId = await CreateAccountAsync(host,
            code: "302",
            name: "Retained Earnings (Wrong Section)",
            type: AccountType.Equity,
            section: StatementSection.Assets,
            isContra: false,
            isActive: true);

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        Func<Task> act = () => svc.CloseFiscalYearAsync(
            EndPeriod,
            retainedAccountId,
            closedBy: "it",
            ct: CancellationToken.None);

        (await act.Should().ThrowAsync<NgbArgumentInvalidException>())
            .Which.Message.Should().Contain("must belong to Equity");
    }

    [Fact]
    public async Task CloseFiscalYear_WhenRetainedEarningsNotCreditNormal_Throws()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Contra under Equity flips the normal balance to Debit.
        var retainedAccountId = await CreateAccountAsync(host,
            code: "303",
            name: "Retained Earnings (Contra)",
            type: AccountType.Equity,
            section: StatementSection.Equity,
            isContra: true,
            isActive: true);

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        Func<Task> act = () => svc.CloseFiscalYearAsync(
            EndPeriod,
            retainedAccountId,
            closedBy: "it",
            ct: CancellationToken.None);

        (await act.Should().ThrowAsync<NgbArgumentInvalidException>())
            .Which.Message.Should().ContainEquivalentOf("must be credit-normal");
    }

    [Fact]
    public async Task CloseFiscalYear_WhenRetainedEarningsRequiresDimensions_ThrowsValidation()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var createScope = host.Services.CreateAsyncScope();
        var accounts = createScope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        var retainedAccountId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "304",
            Name: "Retained Earnings (Dim Required)",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity,
            IsContra: false,
            IsActive: true,
            DimensionRules:
            [
                new AccountDimensionRuleRequest("buildings", IsRequired: true, Ordinal: 10)
            ]), CancellationToken.None);

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        Func<Task> act = () => svc.CloseFiscalYearAsync(
            EndPeriod,
            retainedAccountId,
            closedBy: "it",
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<FiscalYearRetainedEarningsValidationException>();
        ex.Which.ErrorCode.Should().Be("period.fiscal_year.retained_earnings_dimensions_not_allowed");
    }

    private static async Task<Guid> CreateAccountAsync(
        IHost host,
        string code,
        string name,
        AccountType type,
        StatementSection section,
        bool isContra,
        bool isActive)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        return await accounts.CreateAsync(new CreateAccountRequest(
            Code: code,
            Name: name,
            Type: type,
            StatementSection: section,
            IsContra: isContra,
            IsActive: isActive,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);
    }
}
