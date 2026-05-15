import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { buildBaselineProfile } from '../../../ngb-performance-tests-framework/src/profiles/baseline.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import type { NgbAuthSetupData } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { pmPlatformAuditFlow } from '../flows/pmPlatformAuditFlow.ts';

export const options = buildBaselineProfile({
  exec: 'audit',
  scenarioName: 'audit',
  tags: { vertical: 'property-management', scenario: 'pm.audit' },
});

export function setup(): NgbAuthSetupData {
  return setupNgbAccessToken();
}

export function audit(data: NgbAuthSetupData): void {
  pmPlatformAuditFlow(getNgbScenarioContext(data));
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}
