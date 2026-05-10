import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { buildSmokeProfile } from '../../../ngb-performance-tests-framework/src/profiles/smoke.ts';
import { createNgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import { pmSmokeFlow } from '../flows/pmSmokeFlow.ts';

export const options = buildSmokeProfile({
  exec: 'pmSmoke',
  tags: { vertical: 'property-management', scenario: 'pm.smoke' },
});

const context = createNgbScenarioContext();

export function pmSmoke(): void {
  pmSmokeFlow(context);
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}
