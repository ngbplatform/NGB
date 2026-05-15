import type { Options, Scenario } from 'k6/options';

import { withSummaryTrendStats } from '../core/summary.ts';
import {
  commonThresholds,
  diagnosticBreakdownThresholds,
  type DiagnosticBreakdownSelector,
  mergeThresholds,
  operationThresholds,
  reportExecutionBreakdownThresholds,
} from '../profiles/thresholds.ts';

export type WorkloadScenario = Scenario;

export interface BusinessDayWorkloadArgs {
  readonly reportBreakdownIds?: readonly string[];
  readonly diagnosticBreakdowns?: readonly DiagnosticBreakdownSelector[];
  readonly profileName?: string;
}

export function buildBusinessDayWorkload(
  scenarios: Record<string, WorkloadScenario>,
  args: BusinessDayWorkloadArgs = {},
): Options {
  const profileName = args.profileName ?? 'business-day';
  return withSummaryTrendStats({
    scenarios,
    thresholds: mergeThresholds(
      commonThresholds,
      operationThresholds,
      {
        [`http_req_failed{profile:${profileName}}`]: ['rate<0.02'],
        [`checks{profile:${profileName}}`]: ['rate>0.98'],
        [`dropped_iterations{profile:${profileName}}`]: ['count<1'],
      },
      reportExecutionBreakdownThresholds(args.reportBreakdownIds),
      diagnosticBreakdownThresholds(args.diagnosticBreakdowns),
    ),
  });
}
