import exec from 'k6/execution';
import { defaultHandleSummary, withSummaryTrendStats } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { buildCapacityProfile } from '../../../ngb-performance-tests-framework/src/profiles/capacity.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import type { NgbAuthSetupData } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_REPORT_BREAKDOWN_IDS } from '../clients/pmReportIds.ts';
import { pmPlatformMaxCapabilityFlow } from '../flows/pmPlatformMixedFlow.ts';
import { PM_PLATFORM_READ_DIAGNOSTIC_BREAKDOWNS } from '../flows/pmPlatformReadFlow.ts';

const profile = buildCapacityProfile({
  exec: 'contention', scenarioName: 'platform_contention',
  tags: { vertical: 'property-management', scenario: 'pm.platform_contention' },
  reportBreakdownIds: PM_REPORT_BREAKDOWN_IDS,
  diagnosticBreakdowns: PM_PLATFORM_READ_DIAGNOSTIC_BREAKDOWNS,
});
export const options = withSummaryTrendStats({ ...profile, thresholds: { ...profile.thresholds,
  'checks{operation:pm.rent_charge.post}': ['rate==1'],
  ngb_pm_posting_succeeded: ['count>0'],
} });

export function setup(): NgbAuthSetupData { return setupNgbAccessToken(); }

export function contention(data: NgbAuthSetupData): void {
  // Emitted once across all VUs. The controller schedules against k6's actual
  // scenario start, excluding compilation, authentication setup and preflight.
  if (exec.scenario.iterationInTest === 0) console.log(`NGB_CONTENTION_ORIGIN=${exec.scenario.startTime}`);
  pmPlatformMaxCapabilityFlow(getNgbScenarioContext(data));
}

export function handleSummary(data: unknown): Record<string, string> { return defaultHandleSummary(data); }
