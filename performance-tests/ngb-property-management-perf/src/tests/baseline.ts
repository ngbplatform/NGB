import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { buildBaselineProfile } from '../../../ngb-performance-tests-framework/src/profiles/baseline.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import type { NgbAuthSetupData } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { pmAccountingEffectsFlow } from '../flows/pmAccountingEffectsFlow.ts';
import { pmCommandPaletteFlow } from '../flows/pmCommandPaletteFlow.ts';
import { pmDashboardFlow } from '../flows/pmDashboardFlow.ts';
import { pmDocumentFlowReadFlow } from '../flows/pmDocumentFlowReadFlow.ts';
import { pmLeaseBrowseFlow } from '../flows/pmLeaseBrowseFlow.ts';
import { pmReportsFlow } from '../flows/pmReportsFlow.ts';

export const options = buildBaselineProfile({
  exec: 'pmBaseline',
  tags: { vertical: 'property-management', scenario: 'pm.baseline' },
});

export function setup(): NgbAuthSetupData {
  return setupNgbAccessToken();
}

export function pmBaseline(data: NgbAuthSetupData): void {
  const context = getNgbScenarioContext(data);
  pmDashboardFlow(context);
  pmLeaseBrowseFlow(context);
  pmCommandPaletteFlow(context);
  pmReportsFlow(context);
  pmAccountingEffectsFlow(context);
  pmDocumentFlowReadFlow(context);
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}
