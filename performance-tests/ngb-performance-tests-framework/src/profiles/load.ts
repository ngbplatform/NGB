import type { Options } from 'k6/options';

import { withSummaryTrendStats } from '../core/summary.ts';
import {
  commonThresholds,
  diagnosticBreakdownThresholds,
  mergeThresholds,
  operationThresholds,
  reportExecutionBreakdownThresholds,
} from './thresholds.ts';
import type { SingleScenarioProfileArgs } from './smoke.ts';

export function buildLoadProfile(args: SingleScenarioProfileArgs = {}): Options {
  const scenarioName = args.scenarioName ?? 'load';
  return withSummaryTrendStats({
    scenarios: {
      [scenarioName]: {
        executor: 'ramping-arrival-rate',
        startRate: 2,
        timeUnit: '1s',
        preAllocatedVUs: 80,
        maxVUs: 160,
        stages: [
          { duration: '2m', target: 8 },
          { duration: '6m', target: 8 },
          { duration: '2m', target: 0 },
        ],
        exec: args.exec ?? 'default',
        env: { NGB_AUTH_INITIAL_JITTER_SECONDS: '20', ...(args.env ?? {}) },
        tags: { profile: 'load', ...(args.tags ?? {}) },
      },
    },
    thresholds: mergeThresholds(
      commonThresholds,
      operationThresholds,
      { http_reqs: ['rate>1'] },
      reportExecutionBreakdownThresholds(args.reportBreakdownIds),
      diagnosticBreakdownThresholds(args.diagnosticBreakdowns),
    ),
  });
}
