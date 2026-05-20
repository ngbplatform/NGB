import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { platformSmokeFlow } from '../../../ngb-performance-tests-framework/src/flows/platformSmokeFlow.ts';
import { buildSmokeProfile } from '../../../ngb-performance-tests-framework/src/profiles/smoke.ts';
import { createNgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';

export const options = buildSmokeProfile({
  exec: 'agencyBillingSmoke',
  tags: { vertical: 'agency-billing', scenario: 'agency_billing.smoke' },
});

const context = createNgbScenarioContext();

export function agencyBillingSmoke(): void {
  platformSmokeFlow(context);
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}
