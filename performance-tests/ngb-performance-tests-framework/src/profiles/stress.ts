import type { Options } from 'k6/options';

import { withSummaryTrendStats } from '../core/summary.ts';
import { diagnosticOperationThresholds, relaxedThresholds } from './thresholds.ts';
import type { SingleScenarioProfileArgs } from './smoke.ts';

export function buildStressProfile(args: SingleScenarioProfileArgs = {}): Options {
  const scenarioName = args.scenarioName ?? 'stress';
  return withSummaryTrendStats({
    scenarios: {
      [scenarioName]: {
        executor: 'ramping-vus',
        stages: [
          { duration: '2m', target: 10 },
          { duration: '2m', target: 20 },
          { duration: '2m', target: 40 },
          { duration: '3m', target: 0 },
        ],
        gracefulRampDown: '30s',
        exec: args.exec ?? 'default',
        ...(args.env ? { env: args.env } : {}),
        tags: { profile: 'stress', ...(args.tags ?? {}) },
      },
    },
    thresholds: {
      ...relaxedThresholds(),
      ...diagnosticOperationThresholds,
      http_req_duration: ['p(99)<30000'],
      http_reqs: ['rate>0.1'],
    },
  });
}
