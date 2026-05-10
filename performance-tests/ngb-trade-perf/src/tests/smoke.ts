import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { platformSmokeFlow } from '../../../ngb-performance-tests-framework/src/flows/platformSmokeFlow.ts';
import { buildSmokeProfile } from '../../../ngb-performance-tests-framework/src/profiles/smoke.ts';
import { createNgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';

export const options = buildSmokeProfile({
  exec: 'tradeSmoke',
  tags: { vertical: 'trade', scenario: 'trade.smoke' },
});

const context = createNgbScenarioContext();

export function tradeSmoke(): void {
  platformSmokeFlow(context);
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}
