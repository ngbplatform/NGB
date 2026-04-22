using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Application.Abstractions.Services;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Maintenance;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Canonical;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportingArchitectureResidue_P0Tests
{
    [Fact]
    public void ReportingAssemblies_DoNotExposeLegacyCanonicalExecutionTypes()
    {
        typeof(IReportSpecializedPlanExecutor).Assembly
            .GetType("NGB.Application.Abstractions.Services.IReportCanonicalExecutor", throwOnError: false)
            .Should().BeNull();

        typeof(CompositeReportPlanExecutor).Assembly
            .GetType("NGB.Runtime.Reporting.Canonical.CanonicalReportExecutorCatalog", throwOnError: false)
            .Should().BeNull();
    }

    [Fact]
    public void ReportingAssemblies_DoNotExpose_Legacy_AccountCard_And_GeneralLedgerAggregated_ReportContracts()
    {
        typeof(IAccountCardEffectivePagedReportReader).Assembly
            .GetType("NGB.Persistence.Readers.Reports.IAccountCardReportReader", throwOnError: false)
            .Should().BeNull();

        typeof(IAccountCardEffectivePagedReportReader).Assembly
            .GetType("NGB.Persistence.Readers.Reports.IAccountCardPagedReportReader", throwOnError: false)
            .Should().BeNull();

        typeof(IAccountCardEffectivePagedReportReader).Assembly
            .GetType("NGB.Persistence.Readers.Reports.IAccountCardGroupedReportReader", throwOnError: false)
            .Should().BeNull();

        typeof(IGeneralLedgerAggregatedPagedReportReader).Assembly
            .GetType("NGB.Persistence.Readers.Reports.IGeneralLedgerAggregatedReader", throwOnError: false)
            .Should().BeNull();

        typeof(IGeneralLedgerAggregatedPagedReportReader).Assembly
            .GetType("NGB.Persistence.Readers.Reports.IGeneralLedgerAggregatedReportReader", throwOnError: false)
            .Should().BeNull();

        typeof(GeneralLedgerAggregatedReportService).Assembly
            .GetType("NGB.Runtime.Reporting.AccountCardReportService", throwOnError: false)
            .Should().BeNull();

        typeof(GeneralLedgerAggregatedReportService).Assembly
            .GetType("NGB.Runtime.Reporting.AccountCardPagedReportService", throwOnError: false)
            .Should().BeNull();

        typeof(GeneralLedgerAggregatedReportService).Assembly
            .GetType("NGB.Runtime.Reporting.AccountCardGroupedReportService", throwOnError: false)
            .Should().BeNull();

        typeof(IAccountCardEffectivePageReader)
            .GetMethod("GetTotalsAsync")
            .Should().BeNull();

        typeof(IAccountCardEffectivePageReader).Assembly
            .GetType("NGB.Accounting.Reports.AccountCard.AccountCardEffectiveTotals", throwOnError: false)
            .Should().BeNull();

        typeof(IAccountCardEffectivePageReader).Assembly
            .GetType("NGB.Accounting.Reports.AccountCard.AccountCardEffectiveTotalsRequest", throwOnError: false)
            .Should().BeNull();

        typeof(AccountCardCanonicalReportExecutor).Assembly
            .GetType("NGB.Runtime.Reporting.Canonical.AccountCardEffectiveViewReducer", throwOnError: false)
            .Should().BeNull();
    }

    [Fact]
    public void GeneralLedgerAggregated_RuntimeService_Uses_Specialized_SnapshotReader_And_DoesNot_Depend_On_Generic_Balance_Or_Turnover_Readers()
    {
        typeof(GeneralLedgerAggregatedReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().Contain(typeof(IGeneralLedgerAggregatedSnapshotReader));

        typeof(GeneralLedgerAggregatedReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().NotContain(typeof(IAccountingBalanceReader));

        typeof(GeneralLedgerAggregatedReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().NotContain(typeof(IAccountingTurnoverReader));
    }

    [Fact]
    public void Shared_ReportPage_Uses_Backend_Metadata_For_Report_Presentation_Instead_Of_Frontend_ReportCode_Registry()
    {
        var repoRoot = FindRepoRoot();
        File.Exists(Path.Combine(repoRoot, "ui", "ngb-property-management-web", "src", "reporting", "reportUiPolicy.ts"))
            .Should().BeFalse();
        File.Exists(Path.Combine(repoRoot, "ui", "ngb-property-management-web", "src", "pages", "ReportPage.vue"))
            .Should().BeFalse();

        var reportPage = File.ReadAllText(Path.Combine(repoRoot, "ui", "ngb-ui-framework", "src", "ngb", "reporting", "NgbReportPage.vue"));
        reportPage.Should().NotContain("resolveReportUiPolicy");
        reportPage.Should().Contain("definition.value?.presentation");
    }

    [Fact]
    public void TrialBalance_RuntimeServices_Use_Specialized_SnapshotReader_And_DoNotExpose_Legacy_AggregationBuilder()
    {
        typeof(TrialBalanceService).Assembly
            .GetType("NGB.Runtime.Reporting.Internal.TrialBalanceAggregationBuilder", throwOnError: false)
            .Should().BeNull();

        typeof(TrialBalanceService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().Contain(typeof(ITrialBalanceSnapshotReader));

        typeof(TrialBalanceService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().NotContain(typeof(IAccountingBalanceReader));

        typeof(TrialBalanceService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().NotContain(typeof(IAccountingTurnoverReader));

        typeof(TrialBalanceReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().Contain(typeof(ITrialBalanceSnapshotReader));

        typeof(TrialBalanceReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().NotContain(typeof(IAccountingBalanceReader));

        typeof(TrialBalanceReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().NotContain(typeof(IAccountingTurnoverReader));
    }

    [Fact]
    public void IncomeStatement_RuntimeService_Uses_Specialized_SnapshotReader_And_DoesNot_Depend_On_TrialBalance_Or_Full_ChartSnapshot()
    {
        typeof(IncomeStatementReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().Contain(typeof(IIncomeStatementSnapshotReader));

        typeof(IncomeStatementReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().NotContain(typeof(ITrialBalanceReader));

        typeof(IncomeStatementReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().NotContain(typeof(IChartOfAccountsProvider));

        typeof(IncomeStatementReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().NotContain(typeof(IAccountByIdResolver));
    }

    [Fact]
    public void StatementOfChangesInEquity_RuntimeService_Uses_Specialized_SnapshotReader_And_DoesNot_Compose_Other_Statements_Or_Generic_Readers()
    {
        typeof(StatementOfChangesInEquityReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().Contain(typeof(IStatementOfChangesInEquitySnapshotReader));

        typeof(StatementOfChangesInEquityReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().NotContain(typeof(IAccountingBalanceReader));

        typeof(StatementOfChangesInEquityReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().NotContain(typeof(IAccountingTurnoverReader));

        typeof(StatementOfChangesInEquityReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().NotContain(typeof(IBalanceSheetReportReader));

        typeof(StatementOfChangesInEquityReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().NotContain(typeof(IIncomeStatementReportReader));
    }

    [Fact]
    public void CashFlowIndirect_RuntimeService_Uses_Specialized_SnapshotReader_And_DoesNot_Compose_Other_Statements_Or_Generic_Readers()
    {
        typeof(CashFlowIndirectReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().Contain(typeof(ICashFlowIndirectSnapshotReader));

        typeof(CashFlowIndirectReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().NotContain(typeof(IAccountingBalanceReader));

        typeof(CashFlowIndirectReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().NotContain(typeof(IAccountingTurnoverReader));

        typeof(CashFlowIndirectReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().NotContain(typeof(IBalanceSheetReportReader));

        typeof(CashFlowIndirectReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().NotContain(typeof(IIncomeStatementReportReader));
    }

    [Fact]
    public void AccountingConsistency_RuntimeConsumers_Use_Single_ReportReader_Contract()
    {
        typeof(AccountingConsistencyCanonicalReportExecutor)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().Contain(typeof(IAccountingConsistencyReportReader));

        typeof(AccountingConsistencyCanonicalReportExecutor)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().NotContain(typeof(AccountingConsistencyReportService));

        typeof(AccountingRebuildService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().Contain(typeof(IAccountingConsistencyReportReader));

        typeof(AccountingRebuildService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().NotContain(typeof(AccountingConsistencyReportService));
    }

    [Fact]
    public void AccountingConsistency_RuntimeService_Uses_Specialized_SnapshotReader_And_DoesNot_Depend_On_Generic_Balance_Or_Turnover_Readers()
    {
        typeof(AccountingConsistencyReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().Contain(typeof(IAccountingConsistencySnapshotReader));

        typeof(AccountingConsistencyReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().NotContain(typeof(IAccountingBalanceReader));

        typeof(AccountingConsistencyReportService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().NotContain(typeof(IAccountingTurnoverReader));
    }

    [Fact]
    public void LedgerAnalysis_RuntimeExecutor_Uses_Specialized_FlatDetailReader_And_Delegates_Back_To_Tabular_Path()
    {
        typeof(LedgerAnalysisComposableReportExecutor)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().Contain(typeof(ILedgerAnalysisFlatDetailReader));

        typeof(LedgerAnalysisComposableReportExecutor)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(x => x.ParameterType)
            .Should().Contain(typeof(ITabularReportPlanExecutor));
    }

    [Fact]
    public void GeneralJournal_RuntimeContracts_DoNotExpose_Legacy_GrandTotals_Model_And_Readers_Stay_PageOnly()
    {
        typeof(IGeneralJournalReader).Assembly
            .GetType("NGB.Accounting.Reports.GeneralJournal.GeneralJournalReportPage", throwOnError: false)
            .Should().BeNull();

        typeof(IGeneralJournalReader).Assembly
            .GetType("NGB.Accounting.Reports.GeneralJournal.GeneralJournalReportTotals", throwOnError: false)
            .Should().BeNull();

        typeof(IGeneralJournalReader).Assembly
            .GetType("NGB.Accounting.Reports.GeneralJournal.GeneralJournalTotalsRequest", throwOnError: false)
            .Should().BeNull();

        typeof(IGeneralJournalReader)
            .GetMethod("GetTotalsAsync")
            .Should().BeNull();

        typeof(IGeneralJournalReportReader)
            .GetMethods()
            .Single(x => x.Name == "GetPageAsync")
            .ReturnType
            .Should()
            .Be(typeof(Task<GeneralJournalPage>));
    }

    [Fact]
    public void PlatformReportingSource_DoesNotContainPropertyManagementLiterals()
    {
        var repoRoot = FindRepoRoot();
        var files = EnumeratePlatformReportingFiles(repoRoot).ToList();

        files.Should().NotBeEmpty();

        var offenders = files
            .Where(file => ContainsVerticalLiteral(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(repoRoot, file))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty("platform reporting code must stay vertical-agnostic");
    }

    [Fact]
    public void ReportingSource_DoesNotContain_Legacy_DrilldownToken_Model_Or_Navigation_Helper_Names()
    {
        var repoRoot = FindRepoRoot();
        var files = EnumerateTypedActionGuardedFiles(repoRoot).ToList();

        files.Should().NotBeEmpty();

        var offenders = files
            .Where(file => ContainsLegacyDrilldownResidue(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(repoRoot, file))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty("typed report cell actions should be the only drilldown/navigation contract");
    }

    private static IEnumerable<string> EnumeratePlatformReportingFiles(string repoRoot)
        => EnumerateSourceFiles(repoRoot,
        [
            Path.Combine(repoRoot, "NGB.Application.Abstractions", "Services"),
            Path.Combine(repoRoot, "NGB.Runtime", "Reporting"),
            Path.Combine(repoRoot, "NGB.Runtime", "DependencyInjection")
        ]);

    private static IEnumerable<string> EnumerateTypedActionGuardedFiles(string repoRoot)
        => EnumerateSourceFiles(repoRoot,
        [
            Path.Combine(repoRoot, "NGB.Contracts", "Reporting"),
            Path.Combine(repoRoot, "NGB.Application.Abstractions", "Services"),
            Path.Combine(repoRoot, "NGB.Runtime", "Reporting"),
            Path.Combine(repoRoot, "NGB.Runtime", "DependencyInjection"),
            Path.Combine(repoRoot, "ui", "ngb-property-management-web", "src", "components", "reporting"),
            Path.Combine(repoRoot, "ui", "ngb-property-management-web", "src", "reporting")
        ]);

    private static IEnumerable<string> EnumerateSourceFiles(string repoRoot, IReadOnlyCollection<string> directories)
    {
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
            {
                if (!IsGuardedSourceFile(file))
                    continue;

                var relative = Path.GetRelativePath(repoRoot, file);
                if (relative.Contains(".Tests", StringComparison.Ordinal)
                    || relative.Contains("__MACOSX", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static bool IsGuardedSourceFile(string path)
        => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".vue", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsVerticalLiteral(string source)
        => source.Contains("pm.property", StringComparison.Ordinal)
           || source.Contains("pm.lease", StringComparison.Ordinal)
           || source.Contains("pm.party", StringComparison.Ordinal);

    private static bool ContainsLegacyDrilldownResidue(string source)
        => source.Contains("DrilldownToken", StringComparison.Ordinal)
           || source.Contains("drilldownToken", StringComparison.Ordinal)
           || source.Contains("buildReportDrilldownUrl", StringComparison.Ordinal)
           || source.Contains("reportDrilldownNavigation", StringComparison.Ordinal);

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "NGB.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root for reporting architecture guard tests.");
    }
}
