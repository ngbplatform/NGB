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

export function buildBaselineProfile(args: SingleScenarioProfileArgs = {}): Options {
  const scenarioName = args.scenarioName ?? 'baseline';
  return withSummaryTrendStats({
    scenarios: {
      [scenarioName]: {
        executor: 'ramping-vus',
        stages: [
          { duration: '30s', target: 5 },
          { duration: '4m', target: 5 },
          { duration: '30s', target: 0 },
        ],
        gracefulRampDown: '30s',
        exec: args.exec ?? 'default',
        env: { NGB_AUTH_INITIAL_JITTER_SECONDS: '10', ...(args.env ?? {}) },
        tags: { profile: 'baseline', ...(args.tags ?? {}) },
      },
    },
    thresholds: mergeThresholds(
      commonThresholds,
      operationThresholds,
      reportExecutionBreakdownThresholds(args.reportBreakdownIds),
      diagnosticBreakdownThresholds(args.diagnosticBreakdowns),
    ),
  });
}
