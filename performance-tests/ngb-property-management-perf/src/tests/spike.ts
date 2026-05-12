import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { buildSpikeProfile } from '../../../ngb-performance-tests-framework/src/profiles/spike.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import type { NgbAuthSetupData } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { pmLeaseBrowseFlow } from '../flows/pmLeaseBrowseFlow.ts';

export const options = buildSpikeProfile({
  exec: 'pmSpike',
  tags: { vertical: 'property-management', scenario: 'pm.spike' },
});

export function setup(): NgbAuthSetupData {
  return setupNgbAccessToken();
}

export function pmSpike(data: NgbAuthSetupData): void {
  const context = getNgbScenarioContext(data);
  pmLeaseBrowseFlow(context);
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}
