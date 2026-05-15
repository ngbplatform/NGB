import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { buildBaselineProfile } from '../../../ngb-performance-tests-framework/src/profiles/baseline.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import type { NgbAuthSetupData } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_REPORT_BREAKDOWN_IDS } from '../clients/pmReportIds.ts';
import { pmReportsFlow } from '../flows/pmReportsFlow.ts';

export const options = buildBaselineProfile({
  exec: 'platformReporting',
  scenarioName: 'platform_reporting',
  tags: { vertical: 'property-management', scenario: 'pm.platform_reporting' },
  reportBreakdownIds: PM_REPORT_BREAKDOWN_IDS,
});

export function setup(): NgbAuthSetupData {
  return setupNgbAccessToken();
}

export function platformReporting(data: NgbAuthSetupData): void {
  pmReportsFlow(getNgbScenarioContext(data), {
    periodProfiles: ['open', 'closed', 'long'],
    includeAccountScopedReports: true,
    includeLedgerAnalysisVariants: true,
    includeXlsxExport: true,
  });
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}
