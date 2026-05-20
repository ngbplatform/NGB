import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { buildBaselineProfile } from '../../../ngb-performance-tests-framework/src/profiles/baseline.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import type { NgbAuthSetupData } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_REPORT_BREAKDOWN_IDS } from '../clients/pmReportIds.ts';
import { pmReportsFlow } from '../flows/pmReportsFlow.ts';

export const options = buildBaselineProfile({
  exec: 'reportingRegression',
  scenarioName: 'reporting_regression',
  tags: { vertical: 'property-management', scenario: 'pm.reporting_regression' },
  reportBreakdownIds: PM_REPORT_BREAKDOWN_IDS,
});

export function setup(): NgbAuthSetupData {
  return setupNgbAccessToken();
}

export function reportingRegression(data: NgbAuthSetupData): void {
  const context = getNgbScenarioContext(data);
  pmReportsFlow(context, {
    periodProfiles: ['open', 'closed', 'long'],
    includeAccountScopedReports: true,
    includeLedgerAnalysisVariants: true,
    includeXlsxExport: true,
  });
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}
