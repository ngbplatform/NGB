import type { Options } from 'k6/options';

import { withSummaryTrendStats } from '../core/summary.ts';
import { commonThresholds, mergeThresholds, operationThresholds } from './thresholds.ts';

export interface SingleScenarioProfileArgs {
  readonly exec?: string;
  readonly scenarioName?: string;
  readonly tags?: Record<string, string>;
  readonly env?: Record<string, string>;
}

export function buildSmokeProfile(args: SingleScenarioProfileArgs = {}): Options {
  const scenarioName = args.scenarioName ?? 'smoke';
  return withSummaryTrendStats({
    scenarios: {
      [scenarioName]: {
        executor: 'shared-iterations',
        vus: 1,
        iterations: 1,
        maxDuration: '1m',
        exec: args.exec ?? 'default',
        ...(args.env ? { env: args.env } : {}),
        tags: { profile: 'smoke', ...(args.tags ?? {}) },
      },
    },
    thresholds: mergeThresholds(commonThresholds, operationThresholds),
  });
}
