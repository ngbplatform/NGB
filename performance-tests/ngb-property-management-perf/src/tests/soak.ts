import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { buildSoakProfile } from '../../../ngb-performance-tests-framework/src/profiles/soak.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import type { NgbAuthSetupData } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { pmCommandPaletteFlow } from '../flows/pmCommandPaletteFlow.ts';
import { pmDashboardFlow } from '../flows/pmDashboardFlow.ts';
import { pmLeaseBrowseFlow } from '../flows/pmLeaseBrowseFlow.ts';

export const options = buildSoakProfile({
  exec: 'pmSoak',
  tags: { vertical: 'property-management', scenario: 'pm.soak' },
});

export function setup(): NgbAuthSetupData {
  return setupNgbAccessToken();
}

export function pmSoak(data: NgbAuthSetupData): void {
  const context = getNgbScenarioContext(data);
  pmDashboardFlow(context);
  pmLeaseBrowseFlow(context);
  pmCommandPaletteFlow(context);
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}
