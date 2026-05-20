import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { buildBreakpointProfile } from '../../../ngb-performance-tests-framework/src/profiles/breakpoint.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import type { NgbAuthSetupData } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_REPORT_BREAKDOWN_IDS } from '../clients/pmReportIds.ts';
import { pmPlatformMaxCapabilityFlow } from '../flows/pmPlatformMixedFlow.ts';
import { PM_PLATFORM_READ_DIAGNOSTIC_BREAKDOWNS } from '../flows/pmPlatformReadFlow.ts';

export const options = buildBreakpointProfile({
  exec: 'platformBreakpoint',
  scenarioName: 'platform_breakpoint',
  tags: { vertical: 'property-management', scenario: 'pm.platform_breakpoint' },
  reportBreakdownIds: PM_REPORT_BREAKDOWN_IDS,
  diagnosticBreakdowns: PM_PLATFORM_READ_DIAGNOSTIC_BREAKDOWNS,
});

export function setup(): NgbAuthSetupData {
  return setupNgbAccessToken();
}

export function platformBreakpoint(data: NgbAuthSetupData): void {
  pmPlatformMaxCapabilityFlow(getNgbScenarioContext(data));
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}
