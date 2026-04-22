using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Reports.IncomeStatement;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class IncomeStatement_Semantics_P1Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task IncomeStatement_IncludeZeroLines_False_ExcludesZeroAccounts()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Seed CoA but do NOT post anything => all accounts are zero.
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IIncomeStatementReportReader>();

        var report = await reader.GetAsync(new IncomeStatementReportRequest
        {
            FromInclusive = ReportingTestHelpers.Period,
            ToInclusive = ReportingTestHelpers.Period,
            IncludeZeroLines = false
        }, CancellationToken.None);

        var lines = report.Sections.SelectMany(s => s.Lines).ToList();

        lines.Should().BeEmpty("IncludeZeroLines=false should return no lines when there are no movements");
        report.NetIncome.Should().Be(0);
    }

    [Fact]
    public async Task IncomeStatement_IncludeZeroLines_True_IncludesZeroAccounts()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IIncomeStatementReportReader>();

        var report = await reader.GetAsync(new IncomeStatementReportRequest
        {
            FromInclusive = ReportingTestHelpers.Period,
            ToInclusive = ReportingTestHelpers.Period,
            IncludeZeroLines = true
        }, CancellationToken.None);

        var lines = report.Sections.SelectMany(s => s.Lines).ToList();

        lines.Should().NotBeEmpty("IncludeZeroLines=true should include lines even when amounts are zero");
        lines.All(l => l.Amount == 0m).Should().BeTrue();
        report.NetIncome.Should().Be(0);
    }

    [Fact]
    public async Task IncomeStatement_RangeAcrossTwoMonths_SumsCorrectly()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var janDoc = Guid.CreateVersion7();
        var febDoc = Guid.CreateVersion7();

        await ReportingTestHelpers.PostAsync(host, janDoc, ReportingTestHelpers.Day1Utc, "50", "90.1", 100m);
        await ReportingTestHelpers.PostAsync(host, febDoc, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), "50", "90.1", 50m);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IIncomeStatementReportReader>();

        var report = await reader.GetAsync(new IncomeStatementReportRequest
        {
            FromInclusive = ReportingTestHelpers.Period,
            ToInclusive = ReportingTestHelpers.NextPeriod,
            IncludeZeroLines = false
        }, CancellationToken.None);

        // Net income: revenue is credit-normal, so income statement usually returns positive revenue as +amount and net income positive.
        // In this model existing golden tests treat Revenue closing balance as negative on TB, but P&L amount is typically positive.
        report.NetIncome.Should().Be(150m);
    }
}
