import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { buildLoadProfile } from '../../../ngb-performance-tests-framework/src/profiles/load.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import type { NgbAuthSetupData } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { pmCommandPaletteFlow } from '../flows/pmCommandPaletteFlow.ts';
import { pmDashboardFlow } from '../flows/pmDashboardFlow.ts';
import { pmLeaseBrowseFlow } from '../flows/pmLeaseBrowseFlow.ts';

export const options = buildLoadProfile({
  exec: 'pmLoad',
  tags: { vertical: 'property-management', scenario: 'pm.load' },
});

export function setup(): NgbAuthSetupData {
  return setupNgbAccessToken();
}

export function pmLoad(data: NgbAuthSetupData): void {
  const context = getNgbScenarioContext(data);
  pmDashboardFlow(context);
  pmLeaseBrowseFlow(context);
  pmCommandPaletteFlow(context);
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}
