import type { Options } from 'k6/options';

import { withSummaryTrendStats } from '../core/summary.ts';
import { diagnosticOperationThresholds, relaxedThresholds } from './thresholds.ts';
import type { SingleScenarioProfileArgs } from './smoke.ts';

export function buildSpikeProfile(args: SingleScenarioProfileArgs = {}): Options {
  const scenarioName = args.scenarioName ?? 'spike';
  return withSummaryTrendStats({
    scenarios: {
      [scenarioName]: {
        executor: 'ramping-vus',
        stages: [
          { duration: '30s', target: 10 },
          { duration: '30s', target: 80 },
          { duration: '2m', target: 80 },
          { duration: '30s', target: 10 },
          { duration: '2m', target: 10 },
          { duration: '30s', target: 0 },
        ],
        exec: args.exec ?? 'default',
        ...(args.env ? { env: args.env } : {}),
        tags: { profile: 'spike', ...(args.tags ?? {}) },
      },
    },
    thresholds: {
      ...relaxedThresholds(),
      ...diagnosticOperationThresholds,
      http_reqs: ['rate>0.1'],
      checks: ['rate>0.90'],
    },
  });
}
