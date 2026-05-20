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

export interface LoadProfileArgs extends SingleScenarioProfileArgs {
  readonly startRate?: number;
  readonly targetRate?: number;
  readonly timeUnit?: string;
  readonly preAllocatedVUs?: number;
  readonly maxVUs?: number;
  readonly rampUpDuration?: string;
  readonly holdDuration?: string;
  readonly rampDownDuration?: string;
}

const DEFAULT_START_RATE = 2;
const DEFAULT_TARGET_RATE = 8;
const DEFAULT_TIME_UNIT = '1s';
const DEFAULT_PRE_ALLOCATED_VUS = 128;
const DEFAULT_MAX_VUS = 256;
const DEFAULT_RAMP_UP_DURATION = '2m';
const DEFAULT_HOLD_DURATION = '6m';
const DEFAULT_RAMP_DOWN_DURATION = '2m';

export function buildLoadProfile(args: LoadProfileArgs = {}): Options {
  const scenarioName = args.scenarioName ?? 'load';
  const targetRate = args.targetRate ?? readNonNegativeRate('NGB_LOAD_TARGET_RATE', DEFAULT_TARGET_RATE);
  const defaultStartRate = targetRate === 0 ? 0 : Math.min(DEFAULT_START_RATE, targetRate);
  const startRate = args.startRate ?? readNonNegativeRate('NGB_LOAD_START_RATE', defaultStartRate);
  const preAllocatedVUs =
    args.preAllocatedVUs ?? readPositiveInteger('NGB_LOAD_PRE_ALLOCATED_VUS', DEFAULT_PRE_ALLOCATED_VUS);
  const maxVUs = args.maxVUs ?? readPositiveInteger('NGB_LOAD_MAX_VUS', DEFAULT_MAX_VUS);
  const rampUpDuration = args.rampUpDuration ?? readEnvDuration('NGB_LOAD_RAMP_UP_DURATION') ?? DEFAULT_RAMP_UP_DURATION;
  const holdDuration = args.holdDuration ?? readEnvDuration('NGB_LOAD_HOLD_DURATION') ?? DEFAULT_HOLD_DURATION;
  const rampDownDuration =
    args.rampDownDuration ?? readEnvDuration('NGB_LOAD_RAMP_DOWN_DURATION') ?? DEFAULT_RAMP_DOWN_DURATION;

  if (maxVUs < preAllocatedVUs) {
    throw new Error(`NGB load profile requires maxVUs (${maxVUs}) to be >= preAllocatedVUs (${preAllocatedVUs}).`);
  }

  return withSummaryTrendStats({
    scenarios: {
      [scenarioName]: {
        executor: 'ramping-arrival-rate',
        startRate,
        timeUnit: args.timeUnit ?? readEnvDuration('NGB_LOAD_TIME_UNIT') ?? DEFAULT_TIME_UNIT,
        preAllocatedVUs,
        maxVUs,
        stages: [
          { duration: rampUpDuration, target: targetRate },
          { duration: holdDuration, target: targetRate },
          { duration: rampDownDuration, target: 0 },
        ],
        exec: args.exec ?? 'default',
        env: { NGB_AUTH_INITIAL_JITTER_SECONDS: '20', ...(args.env ?? {}) },
        tags: { profile: 'load', ...(args.tags ?? {}) },
      },
    },
    thresholds: mergeThresholds(
      commonThresholds,
      operationThresholds,
      {
        dropped_iterations: ['count<1'],
        http_reqs: ['rate>1'],
      },
      reportExecutionBreakdownThresholds(args.reportBreakdownIds),
      diagnosticBreakdownThresholds(args.diagnosticBreakdowns),
    ),
  });
}

function readPositiveInteger(name: string, fallback: number): number {
  const raw = __ENV[name]?.trim();
  if (!raw) {
    return fallback;
  }

  const value = Number(raw);
  if (!Number.isInteger(value) || value <= 0) {
    throw new Error(`${name} must be a positive integer, got ${JSON.stringify(raw)}.`);
  }

  return value;
}

function readNonNegativeRate(name: string, fallback: number): number {
  const value = readRate(name);
  return value === undefined ? fallback : assertNonNegativeRate(name, value);
}

function readRate(name: string): number | undefined {
  const raw = __ENV[name]?.trim();
  if (!raw) {
    return undefined;
  }

  return Number(raw);
}

function assertNonNegativeRate(name: string, value: number): number {
  if (!Number.isFinite(value) || value < 0) {
    throw new Error(`${name} must be a non-negative number, got ${value}.`);
  }

  return value;
}

function readEnvDuration(name: string): string | undefined {
  const value = __ENV[name]?.trim();
  return value ? value : undefined;
}
