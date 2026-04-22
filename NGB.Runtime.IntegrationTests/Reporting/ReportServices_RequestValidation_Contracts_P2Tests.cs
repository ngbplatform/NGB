using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Reports.BalanceSheet;
using NGB.Accounting.Reports.IncomeStatement;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

/// <summary>
/// P2: Contract tests that lock in request/parameter validation for Runtime report services.
/// These should fail fast (before any DB work) with clear exception types.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReportServices_RequestValidation_Contracts_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TrialBalance_ToLessThanFrom_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var tb = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();

        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2025, 12, 1);

        var act = () => tb.GetAsync(from, to);

        var ex = await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        ex.Which.ParamName.Should().Be("toInclusive");
        ex.Which.Reason.Should().Be("To must be on or after From.");
    }

    [Fact]
    public async Task TrialBalance_FromNotMonthStart_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var tb = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();

        var from = new DateOnly(2026, 1, 2);
        var to = new DateOnly(2026, 2, 1);

        var act = () => tb.GetAsync(from, to);

        var ex = await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        ex.Which.ParamName.Should().Be("fromInclusive");
        ex.Which.Reason.Should().Contain("first day of a month");
    }

    [Fact]
    public async Task TrialBalance_ToNotMonthStart_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var tb = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();

        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 2, 2);

        var act = () => tb.GetAsync(from, to);

        var ex = await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        ex.Which.ParamName.Should().Be("toInclusive");
        ex.Which.Reason.Should().Contain("first day of a month");
    }

    [Fact]
    public async Task BalanceSheet_NullRequest_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var bs = scope.ServiceProvider.GetRequiredService<IBalanceSheetReportReader>();

        var act = () => bs.GetAsync(null!);

        var ex = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        ex.Which.ParamName.Should().Be("request");
    }

    [Fact]
    public async Task BalanceSheet_AsOfNotMonthStart_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var bs = scope.ServiceProvider.GetRequiredService<IBalanceSheetReportReader>();

        var request = new BalanceSheetReportRequest
        {
            AsOfPeriod = new DateOnly(2026, 1, 15)
        };

        var act = () => bs.GetAsync(request);

        var ex = await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        ex.Which.ParamName.Should().Be("AsOfPeriod");
        ex.Which.Reason.Should().Contain("first day of a month");
    }

    [Fact]
    public async Task IncomeStatement_NullRequest_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var pl = scope.ServiceProvider.GetRequiredService<IIncomeStatementReportReader>();

        var act = () => pl.GetAsync(null!);

        var ex = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        ex.Which.ParamName.Should().Be("request");
    }

    [Fact]
    public async Task IncomeStatement_FromNotMonthStart_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var pl = scope.ServiceProvider.GetRequiredService<IIncomeStatementReportReader>();

        var request = new IncomeStatementReportRequest
        {
            FromInclusive = new DateOnly(2026, 1, 2),
            ToInclusive = new DateOnly(2026, 2, 1)
        };

        var act = () => pl.GetAsync(request);

        var ex = await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        ex.Which.ParamName.Should().Be("FromInclusive");
        ex.Which.Reason.Should().Contain("first day of a month");
    }

    [Fact]
    public async Task IncomeStatement_ToLessThanFrom_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var pl = scope.ServiceProvider.GetRequiredService<IIncomeStatementReportReader>();

        var request = new IncomeStatementReportRequest
        {
            FromInclusive = new DateOnly(2026, 2, 1),
            ToInclusive = new DateOnly(2026, 1, 1)
        };

        var act = () => pl.GetAsync(request);

        var ex = await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        ex.Which.ParamName.Should().Be("ToInclusive");
        ex.Which.Reason.Should().Be("To must be on or after From.");
    }
}
