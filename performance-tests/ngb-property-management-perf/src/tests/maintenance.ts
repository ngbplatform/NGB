import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { buildBaselineProfile } from '../../../ngb-performance-tests-framework/src/profiles/baseline.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import type { NgbAuthSetupData } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { pmPlatformMaintenanceFlow } from '../flows/pmPlatformMaintenanceFlow.ts';

export const options = buildBaselineProfile({
  exec: 'maintenance',
  scenarioName: 'maintenance',
  tags: { vertical: 'property-management', scenario: 'pm.maintenance' },
});

export function setup(): NgbAuthSetupData {
  return setupNgbAccessToken();
}

export function maintenance(data: NgbAuthSetupData): void {
  pmPlatformMaintenanceFlow(getNgbScenarioContext(data));
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}
