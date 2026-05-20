import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { buildBaselineProfile } from '../../../ngb-performance-tests-framework/src/profiles/baseline.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import type { NgbAuthSetupData } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { pmDocumentLifecycleFlow } from '../flows/pmDocumentLifecycleFlow.ts';

export const options = buildBaselineProfile({
  exec: 'documentLifecycle',
  scenarioName: 'document_lifecycle',
  tags: { vertical: 'property-management', scenario: 'pm.document_lifecycle' },
});

export function setup(): NgbAuthSetupData {
  return setupNgbAccessToken();
}

export function documentLifecycle(data: NgbAuthSetupData): void {
  pmDocumentLifecycleFlow(getNgbScenarioContext(data));
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}
