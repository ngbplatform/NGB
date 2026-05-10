import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { buildStressProfile } from '../../../ngb-performance-tests-framework/src/profiles/stress.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import type { NgbAuthSetupData } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { pmCommandPaletteFlow } from '../flows/pmCommandPaletteFlow.ts';
import { pmDashboardFlow } from '../flows/pmDashboardFlow.ts';
import { pmLeaseBrowseFlow } from '../flows/pmLeaseBrowseFlow.ts';
import { pmReportsFlow } from '../flows/pmReportsFlow.ts';

export const options = buildStressProfile({
  exec: 'pmStress',
  tags: { vertical: 'property-management', scenario: 'pm.stress' },
});

export function setup(): NgbAuthSetupData {
  return setupNgbAccessToken();
}

export function pmStress(data: NgbAuthSetupData): void {
  const context = getNgbScenarioContext(data);
  pmDashboardFlow(context);
  pmLeaseBrowseFlow(context);
  pmCommandPaletteFlow(context);
  pmReportsFlow(context);
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}
