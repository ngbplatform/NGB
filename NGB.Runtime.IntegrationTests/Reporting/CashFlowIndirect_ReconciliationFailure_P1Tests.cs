using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Accounting.CashFlow;
using NGB.Accounting.Reports.CashFlowIndirect;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Reporting.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class CashFlowIndirect_ReconciliationFailure_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Report_WhenSnapshotDoesNotReconcile_ThrowsConfigurationViolation()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.Replace(ServiceDescriptor.Scoped<ICashFlowIndirectSnapshotReader>(
                    _ => new NonReconcilingCashFlowIndirectSnapshotReader()));
            });

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ICashFlowIndirectReportReader>();

        var act = () => reader.GetAsync(
            new CashFlowIndirectReportRequest
            {
                FromInclusive = new DateOnly(2037, 1, 1),
                ToInclusive = new DateOnly(2037, 1, 1)
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<AccountingReportValidationException>()
            .WithMessage("*failed reconciliation*beginningCash=10*endingCash=150*operating=100*investing=-30*financing=20*Verify cash-flow account classifications and non-cash operating adjustment tags.*");
    }

    private sealed class NonReconcilingCashFlowIndirectSnapshotReader : ICashFlowIndirectSnapshotReader
    {
        public Task<CashFlowIndirectSnapshot> GetAsync(DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct = default)
            => Task.FromResult(
                new CashFlowIndirectSnapshot(
                    NetIncome: 100m,
                    OperatingLines: [],
                    InvestingLines:
                    [
                        new CashFlowIndirectSnapshotLine(
                            CashFlowSection.Investing,
                            "inv_property_equipment_net",
                            "Property and equipment",
                            10,
                            -30m)
                    ],
                    FinancingLines:
                    [
                        new CashFlowIndirectSnapshotLine(
                            CashFlowSection.Financing,
                            "fin_debt_net",
                            "Debt",
                            10,
                            20m)
                    ],
                    BeginningCash: 10m,
                    EndingCash: 150m,
                    BeginningLatestClosedPeriod: null,
                    BeginningRollForwardPeriods: 0,
                    EndingLatestClosedPeriod: null,
                    EndingRollForwardPeriods: 0,
                    UnclassifiedCashRows: []));
    }
}
