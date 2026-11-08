using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NGB.Accounting.Accounts;
using NGB.Accounting.Balances;
using NGB.Persistence.Readers;
using NGB.Runtime.Accounting;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PlatformPostgresCollection.Name)]
public sealed class NegativeBalancePolicy_OpenMonths_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(false, false, NegativeBalancePolicy.Forbid)]
    [InlineData(false, true, NegativeBalancePolicy.Forbid)]
    [InlineData(true, false, NegativeBalancePolicy.Forbid)]
    [InlineData(true, true, NegativeBalancePolicy.Forbid)]
    [InlineData(false, false, NegativeBalancePolicy.Warn)]
    [InlineData(false, true, NegativeBalancePolicy.Warn)]
    [InlineData(true, false, NegativeBalancePolicy.Warn)]
    [InlineData(true, true, NegativeBalancePolicy.Warn)]
    public async Task Post_UsesAllEarlierOpenMonths_ForDebitAndCreditNormalAccounts(
        bool closeJanuary,
        bool isContra,
        NegativeBalancePolicy policy)
    {
        using var sink = new TestLogSink();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
            services.AddLogging(logging => logging.AddProvider(sink)));
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        var accountId = await accounts.CreateAsync(new CreateAccountRequest(
            "50", "Controlled account", AccountType.Asset, StatementSection.Assets,
            IsContra: isContra, NegativeBalancePolicy: policy));
        await accounts.CreateAsync(new CreateAccountRequest(
            "90.1", "Revenue", AccountType.Income, StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow));
        await accounts.CreateAsync(new CreateAccountRequest(
            "91", "Expense", AccountType.Expense, StatementSection.Expenses,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow));

        var january = ReportingTestHelpers.Day15Utc;
        var february = january.AddMonths(1);
        var march = january.AddMonths(2);
        await ReportingTestHelpers.PostAsync(
            host, Guid.NewGuid(), january, isContra ? "91" : "50", isContra ? "50" : "90.1", 100m);
        if (closeJanuary)
            await ReportingTestHelpers.CloseMonthAsync(host, new DateOnly(2026, 1, 1));

        // January's receipt must remain available even when January was never closed.
        await ReportingTestHelpers.PostAsync(
            host, Guid.NewGuid(), february, isContra ? "50" : "91", isContra ? "90.1" : "50", 90m);
        sink.Records.Should().NotContain(record => record.Level == LogLevel.Warning
            && record.Message.Contains("Negative balance projected:", StringComparison.Ordinal));

        var reader = scope.ServiceProvider.GetRequiredService<IAccountingOperationalBalanceReader>();
        var snapshot = (await reader.GetForKeysAsync(
            new DateOnly(2026, 3, 1), [new AccountingBalanceKey(accountId, Guid.Empty)])).Should().ContainSingle().Subject;
        snapshot.PreviousClosingBalance.Should().Be(closeJanuary ? (isContra ? -100m : 100m) : 0m);
        snapshot.DebitTurnover.Should().Be(0m, "the existing field continues to describe the requested month");
        snapshot.CreditTurnover.Should().Be(0m, "the existing field continues to describe the requested month");
        var priorDelta = closeJanuary ? -90m : 10m;
        snapshot.PriorOpenPeriodDelta.Should().Be(isContra ? -priorDelta : priorDelta);

        // February's spend must reduce the available balance in March to 10.
        var documentId = Guid.NewGuid();
        var error = await Record.ExceptionAsync(() => ReportingTestHelpers.PostAsync(
            host, documentId, march, isContra ? "50" : "91", isContra ? "90.1" : "50", 20m));
        var entries = await scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(documentId);
        if (policy == NegativeBalancePolicy.Forbid)
        {
            error.Should().BeOfType<AccountingNegativeBalanceForbiddenException>();
            entries.Should().BeEmpty("the rejected posting must roll back");
        }
        else
        {
            error.Should().BeNull();
            entries.Should().ContainSingle();
            sink.Records.Should().Contain(record => record.Level == LogLevel.Warning
                && record.Message.Contains("Negative balance projected:", StringComparison.Ordinal)
                && record.Message.Contains("= -10 period=2026-03-01", StringComparison.Ordinal));
        }
    }
}
