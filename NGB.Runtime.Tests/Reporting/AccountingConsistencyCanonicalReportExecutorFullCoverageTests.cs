using FluentAssertions;
using Moq;
using NGB.Accounting.Reports.AccountingConsistency;
using NGB.Contracts.Reporting;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Core.Reporting;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Canonical;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class AccountingConsistencyCanonicalReportExecutorFullCoverageTests
{
    [Fact]
    public async Task Execute_NoPreviousPeriodNoIssuesAndHiddenTotals_SkipsDimensionLookups()
    {
        var reader = new Mock<IAccountingConsistencyReportReader>();
        reader
            .Setup(service => service.RunForPeriodAsync(new DateOnly(2026, 8, 1), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountingConsistencyReport { Period = new DateOnly(2026, 8, 1) });
        var dimensionSets = new Mock<IDimensionSetReader>(MockBehavior.Strict);
        var enrichment = new Mock<IDimensionValueEnrichmentReader>(MockBehavior.Strict);
        var sut = new AccountingConsistencyCanonicalReportExecutor(reader.Object, dimensionSets.Object, enrichment.Object);

        var page = await sut.ExecuteAsync(
            Definition(),
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string> { ["period_utc"] = "2026-08-31" },
                Layout: new ReportLayoutDto(ShowGrandTotals: false)),
            default);

        sut.ReportCode.Should().Be(AccountingReportCodes.Consistency);
        page.PrebuiltSheet!.Rows.Should().BeEmpty();
        page.PrebuiltSheet.Meta!.Subtitle.Should().Be("2026-08-31");
        page.Limit.Should().Be(0);
        page.Total.Should().Be(0);
        page.HasMore.Should().BeFalse();
        page.NextCursor.Should().BeNull();
        page.Diagnostics!["executor"].Should().Be("canonical-accounting-consistency");
        dimensionSets.VerifyNoOtherCalls();
        enrichment.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Execute_WithPreviousPeriodIssuesAndTotals_EnrichesDistinctValidDimensionSets()
    {
        var setId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var missingSetId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var dimensionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var valueId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var bag = new DimensionBag([new DimensionValue(dimensionId, valueId)]);
        var report = new AccountingConsistencyReport
        {
            Period = new DateOnly(2026, 8, 1),
            PreviousPeriodForChainCheck = new DateOnly(2026, 7, 1),
            TurnoversVsRegisterDiffCount = long.MaxValue,
            BalanceVsTurnoverMismatchCount = 2,
            BalanceChainMismatchCount = 3,
            MissingKeyCount = 4,
            Issues =
            [
                Issue(AccountingConsistencyIssueKind.TurnoversVsRegisterMismatch, null, null, null),
                Issue(AccountingConsistencyIssueKind.BalanceVsTurnoverMismatch, Guid.Empty, "2000", new DateOnly(2026, 7, 1)),
                Issue(AccountingConsistencyIssueKind.BalanceChainMismatch, missingSetId, "3000", null),
                Issue(AccountingConsistencyIssueKind.MissingKey, setId, "4000", new DateOnly(2026, 7, 1)),
                Issue(AccountingConsistencyIssueKind.MissingKey, setId, "4001", null)
            ]
        };
        var reader = new Mock<IAccountingConsistencyReportReader>();
        reader
            .Setup(service => service.RunForPeriodAsync(
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 7, 1),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);
        var dimensionSets = new Mock<IDimensionSetReader>();
        dimensionSets
            .Setup(service => service.GetBagsByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { missingSetId, setId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag> { [setId] = bag });
        var enrichment = new Mock<IDimensionValueEnrichmentReader>();
        enrichment
            .Setup(service => service.ResolveAsync(
                It.Is<IReadOnlyCollection<DimensionValueKey>>(keys => keys.SequenceEqual(new[] { new DimensionValueKey(dimensionId, valueId) })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<DimensionValueKey, string>
            {
                [new DimensionValueKey(dimensionId, valueId)] = "Cost center A"
            });
        var sut = new AccountingConsistencyCanonicalReportExecutor(reader.Object, dimensionSets.Object, enrichment.Object);

        var page = await sut.ExecuteAsync(
            Definition(),
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>
                {
                    ["period_utc"] = "2026-08-31",
                    ["previous_period_utc"] = "2026-07-20"
                },
                Layout: new ReportLayoutDto(ShowGrandTotals: true)),
            default);

        var sheet = page.PrebuiltSheet!;
        sheet.Meta!.Title.Should().Be("Accounting consistency");
        sheet.Meta.Subtitle.Should().Be("2026-08-31 · previous 2026-07-20");
        sheet.Columns.Should().HaveCount(6);
        sheet.Rows.Should().HaveCount(10);
        sheet.Rows.Take(5).Should().OnlyContain(row => row.RowKind == ReportRowKind.Detail);
        sheet.Rows.Skip(5).Should().OnlyContain(row => row.RowKind == ReportRowKind.Total && row.SemanticRole == "grand_total");
        sheet.Rows[0].Cells[2].Display.Should().BeEmpty();
        sheet.Rows[0].Cells[3].Display.Should().BeEmpty();
        sheet.Rows[0].Cells[4].Display.Should().BeEmpty();
        sheet.Rows[1].Cells[2].Display.Should().Be("2026-07-01");
        sheet.Rows[1].Cells[4].Display.Should().BeEmpty();
        sheet.Rows[2].Cells[4].Display.Should().BeEmpty();
        sheet.Rows[3].Cells[4].Display.Should().Be("Cost center A");
        sheet.Rows[^5].Cells[0].Display.Should().Be("Turnovers vs register");
        sheet.Rows[^5].Cells[1].Display.Should().Be(long.MaxValue.ToString());
        sheet.Rows[^1].Cells[0].Display.Should().Be("Issue count");
        sheet.Rows[^1].Cells[1].Display.Should().Be("5");
        page.Limit.Should().Be(10);
        page.Total.Should().Be(10);
    }

    [Fact]
    public async Task Execute_NullLayout_UsesDefaultGrandTotalsBehavior()
    {
        var reader = new Mock<IAccountingConsistencyReportReader>();
        reader.Setup(service => service.RunForPeriodAsync(It.IsAny<DateOnly>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountingConsistencyReport());
        var sut = new AccountingConsistencyCanonicalReportExecutor(
            reader.Object,
            Mock.Of<IDimensionSetReader>(),
            Mock.Of<IDimensionValueEnrichmentReader>());

        var page = await sut.ExecuteAsync(
            Definition(),
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string> { ["period_utc"] = "2026-01-01" }),
            default);

        page.PrebuiltSheet!.Rows.Should().HaveCount(5);
    }

    [Fact]
    public async Task Execute_RejectsReportsThatExceedTheMaterializationBound()
    {
        var report = new AccountingConsistencyReport
        {
            Issues = Enumerable.Range(0, NGB.Contracts.Common.PagingLimits.MaxMaterializedRows + 1)
                .Select(index => Issue(
                    AccountingConsistencyIssueKind.MissingKey,
                    dimensionSetId: null,
                    accountCode: index.ToString(),
                    previousPeriod: null))
                .ToList()
        };
        var reader = new Mock<IAccountingConsistencyReportReader>();
        reader.Setup(service => service.RunForPeriodAsync(It.IsAny<DateOnly>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);
        var sut = new AccountingConsistencyCanonicalReportExecutor(
            reader.Object,
            Mock.Of<IDimensionSetReader>(MockBehavior.Strict),
            Mock.Of<IDimensionValueEnrichmentReader>(MockBehavior.Strict));

        var act = () => sut.ExecuteAsync(
            Definition(),
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string> { ["period_utc"] = "2026-08-01" },
                Layout: new ReportLayoutDto(ShowGrandTotals: false)),
            default);

        (await act.Should().ThrowAsync<NGB.Tools.Exceptions.NgbArgumentOutOfRangeException>())
            .Which.ParamName.Should().Be("filters");
    }

    private static AccountingConsistencyIssue Issue(
        AccountingConsistencyIssueKind kind,
        Guid? dimensionSetId,
        string? accountCode,
        DateOnly? previousPeriod)
        => new()
        {
            Kind = kind,
            Period = new DateOnly(2026, 8, 1),
            PreviousPeriod = previousPeriod,
            AccountCode = accountCode,
            DimensionSetId = dimensionSetId,
            Message = $"Issue {kind}"
        };

    private static ReportDefinitionDto Definition()
        => new(
            AccountingReportCodes.Consistency,
            "Accounting consistency",
            Parameters:
            [
                new ReportParameterMetadataDto("period_utc", "date", true, Label: "Period"),
                new ReportParameterMetadataDto("previous_period_utc", "date", false, Label: "Previous period")
            ]);
}
