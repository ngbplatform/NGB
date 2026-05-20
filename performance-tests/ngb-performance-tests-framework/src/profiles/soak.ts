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

export function buildSoakProfile(args: SingleScenarioProfileArgs = {}): Options {
  const scenarioName = args.scenarioName ?? 'soak';
  return withSummaryTrendStats({
    scenarios: {
      [scenarioName]: {
        executor: 'ramping-vus',
        stages: [
          { duration: '5m', target: 10 },
          { duration: '30m', target: 10 },
          { duration: '5m', target: 0 },
        ],
        gracefulRampDown: '1m',
        exec: args.exec ?? 'default',
        ...(args.env ? { env: args.env } : {}),
        tags: { profile: 'soak', ...(args.tags ?? {}) },
      },
    },
    thresholds: mergeThresholds(
      commonThresholds,
      operationThresholds,
      {
        http_req_failed: ['rate<0.02'],
        ngb_business_operation_failed: ['rate<0.02'],
      },
      reportExecutionBreakdownThresholds(args.reportBreakdownIds),
      diagnosticBreakdownThresholds(args.diagnosticBreakdowns),
    ),
  });
}
