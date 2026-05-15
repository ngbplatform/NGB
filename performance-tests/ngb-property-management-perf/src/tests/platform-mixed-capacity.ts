import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { buildCapacityProfile } from '../../../ngb-performance-tests-framework/src/profiles/capacity.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import type { NgbAuthSetupData } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_REPORT_BREAKDOWN_IDS } from '../clients/pmReportIds.ts';
import { pmPlatformMaxCapabilityFlow } from '../flows/pmPlatformMixedFlow.ts';
import { PM_PLATFORM_READ_DIAGNOSTIC_BREAKDOWNS } from '../flows/pmPlatformReadFlow.ts';

export const options = buildCapacityProfile({
  exec: 'platformMixedCapacity',
  scenarioName: 'platform_mixed_capacity',
  tags: { vertical: 'property-management', scenario: 'pm.platform_mixed_capacity' },
  reportBreakdownIds: PM_REPORT_BREAKDOWN_IDS,
  diagnosticBreakdowns: PM_PLATFORM_READ_DIAGNOSTIC_BREAKDOWNS,
});

export function setup(): NgbAuthSetupData {
  return setupNgbAccessToken();
}

export function platformMixedCapacity(data: NgbAuthSetupData): void {
  pmPlatformMaxCapabilityFlow(getNgbScenarioContext(data));
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}
