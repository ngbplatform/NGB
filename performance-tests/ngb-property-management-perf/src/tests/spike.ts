import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { buildSpikeProfile } from '../../../ngb-performance-tests-framework/src/profiles/spike.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import type { NgbAuthSetupData } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_REPORT_BREAKDOWN_IDS } from '../clients/pmReportIds.ts';
import { PM_PLATFORM_READ_DIAGNOSTIC_BREAKDOWNS } from '../flows/pmPlatformReadFlow.ts';
import { pmPlatformSpikeFlow } from '../flows/pmPlatformMixedFlow.ts';

export const options = buildSpikeProfile({
  exec: 'pmSpike',
  tags: { vertical: 'property-management', scenario: 'pm.spike' },
  reportBreakdownIds: PM_REPORT_BREAKDOWN_IDS,
  diagnosticBreakdowns: PM_PLATFORM_READ_DIAGNOSTIC_BREAKDOWNS,
});

export function setup(): NgbAuthSetupData {
  return setupNgbAccessToken();
}

export function pmSpike(data: NgbAuthSetupData): void {
  const context = getNgbScenarioContext(data);
  pmPlatformSpikeFlow(context);
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}
