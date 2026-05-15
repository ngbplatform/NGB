import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { buildLoadProfile } from '../../../ngb-performance-tests-framework/src/profiles/load.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import type { NgbAuthSetupData } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_PLATFORM_READ_DIAGNOSTIC_BREAKDOWNS, pmPlatformReadFlow } from '../flows/pmPlatformReadFlow.ts';

export const options = buildLoadProfile({
  exec: 'platformRead',
  scenarioName: 'platform_read',
  tags: { vertical: 'property-management', scenario: 'pm.platform_read' },
  diagnosticBreakdowns: PM_PLATFORM_READ_DIAGNOSTIC_BREAKDOWNS,
});

export function setup(): NgbAuthSetupData {
  return setupNgbAccessToken();
}

export function platformRead(data: NgbAuthSetupData): void {
  pmPlatformReadFlow(getNgbScenarioContext(data), {
    includeMetadata: false,
    includeLookup: true,
    includeDeepPages: true,
  });
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}
