import type { Options, Scenario } from 'k6/options';

import { withSummaryTrendStats } from '../core/summary.ts';
import { commonThresholds, mergeThresholds, operationThresholds } from '../profiles/thresholds.ts';

export type WorkloadScenario = Scenario;

export function buildBusinessDayWorkload(scenarios: Record<string, WorkloadScenario>): Options {
  return withSummaryTrendStats({
    scenarios,
    thresholds: mergeThresholds(commonThresholds, operationThresholds, {
      'http_req_failed{profile:business-day}': ['rate<0.02'],
      'checks{profile:business-day}': ['rate>0.98'],
    }),
  });
}
