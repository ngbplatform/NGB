import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { buildCapacityProfile } from '../../../ngb-performance-tests-framework/src/profiles/capacity.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import type { NgbAuthSetupData } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_PLATFORM_READ_DIAGNOSTIC_BREAKDOWNS, pmPlatformReadFlow } from '../flows/pmPlatformReadFlow.ts';

export const options = buildCapacityProfile({
  exec: 'platformReadCapacity',
  scenarioName: 'platform_read_capacity',
  tags: { vertical: 'property-management', scenario: 'pm.platform_read_capacity' },
  diagnosticBreakdowns: PM_PLATFORM_READ_DIAGNOSTIC_BREAKDOWNS,
});

export function setup(): NgbAuthSetupData {
  return setupNgbAccessToken();
}

export function platformReadCapacity(data: NgbAuthSetupData): void {
  pmPlatformReadFlow(getNgbScenarioContext(data), {
    includeMetadata: false,
    includeLookup: true,
    includeDeepPages: true,
  });
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}
