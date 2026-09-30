import type { Options } from 'k6/options';

import { withSummaryTrendStats } from '../core/summary.ts';
import {
  diagnosticBreakdownThresholds,
  diagnosticOperationThresholds,
  mergeThresholds,
  relaxedThresholds,
  reportExecutionBreakdownThresholds,
} from './thresholds.ts';
import type { SingleScenarioProfileArgs } from './smoke.ts';

export interface BreakpointStage {
  readonly duration: string;
  readonly target: number;
}

export interface BreakpointProfileArgs extends SingleScenarioProfileArgs {
  readonly rates?: readonly number[];
  readonly stages?: readonly BreakpointStage[];
  readonly rampDuration?: string;
  readonly holdDuration?: string;
  readonly rampDownDuration?: string;
  readonly gracefulStop?: string;
  readonly preAllocatedVUs?: number;
  readonly maxVUs?: number;
}

const DEFAULT_BREAKPOINT_RATES = [2, 4, 8, 12, 16, 24, 32] as const;
const DEFAULT_RAMP_DURATION = '2m';
const DEFAULT_HOLD_DURATION = '5m';
const DEFAULT_RAMP_DOWN_DURATION = '3m';
const DEFAULT_GRACEFUL_STOP = '75s';
const DEFAULT_PRE_ALLOCATED_VUS = 500;
const DEFAULT_MAX_VUS = 500;

export function buildBreakpointProfile(args: BreakpointProfileArgs = {}): Options {
  const scenarioName = args.scenarioName ?? 'breakpoint';
  const preAllocatedVUs = resolvePositiveInteger(
    'NGB_BREAKPOINT_PRE_ALLOCATED_VUS', DEFAULT_PRE_ALLOCATED_VUS, args.preAllocatedVUs,
  );
  const maxVUs = resolvePositiveInteger('NGB_BREAKPOINT_MAX_VUS', DEFAULT_MAX_VUS, args.maxVUs);
  const gracefulStop = normalizeDuration(
    args.gracefulStop ?? readEnvDuration('NGB_BREAKPOINT_GRACEFUL_STOP') ?? DEFAULT_GRACEFUL_STOP,
    'gracefulStop',
  );

  if (maxVUs < preAllocatedVUs) {
    throw new Error(`NGB breakpoint profile requires maxVUs (${maxVUs}) to be >= preAllocatedVUs (${preAllocatedVUs}).`);
  }

  return withSummaryTrendStats({
    scenarios: {
      [scenarioName]: {
        executor: 'ramping-arrival-rate',
        startRate: 1,
        timeUnit: '1s',
        // Allocate the pool before measurement so VU initialization cannot skip arrivals.
        preAllocatedVUs,
        maxVUs,
        // Allow a final iteration to finish after initial auth jitter (up to 45s).
        gracefulStop,
        stages: [...resolveBreakpointStages(args)],
        exec: args.exec ?? 'default',
        env: { NGB_AUTH_INITIAL_JITTER_SECONDS: '45', ...(args.env ?? {}) },
        tags: { profile: 'breakpoint', ...(args.tags ?? {}) },
      },
    },
    thresholds: mergeThresholds(
      relaxedThresholds(),
      diagnosticOperationThresholds,
      // Keep running through overload, but do not report a lossy profile as PASS.
      { http_reqs: ['rate>0.1'], dropped_iterations: ['count<1'] },
      reportExecutionBreakdownThresholds(args.reportBreakdownIds),
      diagnosticBreakdownThresholds(args.diagnosticBreakdowns),
    ),
  });
}

export function buildBreakpointStages(args: {
  readonly rates?: readonly number[];
  readonly rampDuration?: string;
  readonly holdDuration?: string;
  readonly rampDownDuration?: string;
} = {}): BreakpointStage[] {
  const rates = normalizeRates(args.rates ?? DEFAULT_BREAKPOINT_RATES);
  const rampDuration = normalizeDuration(args.rampDuration ?? DEFAULT_RAMP_DURATION, 'rampDuration');
  const holdDuration = normalizeDuration(args.holdDuration ?? DEFAULT_HOLD_DURATION, 'holdDuration');
  const rampDownDuration = normalizeDuration(
    args.rampDownDuration ?? DEFAULT_RAMP_DOWN_DURATION,
    'rampDownDuration',
  );

  return [
    ...rates.flatMap((target) => [
      { duration: rampDuration, target },
      { duration: holdDuration, target },
    ]),
    { duration: rampDownDuration, target: 0 },
  ];
}

function resolveBreakpointStages(args: BreakpointProfileArgs): BreakpointStage[] {
  if (args.stages) {
    return validateStages(args.stages);
  }

  const stageArgs: {
    rates?: readonly number[];
    rampDuration?: string;
    holdDuration?: string;
    rampDownDuration?: string;
  } = {};
  const rates = args.rates ?? readBreakpointRates(__ENV.NGB_BREAKPOINT_RATES);
  const rampDuration = args.rampDuration ?? readEnvDuration('NGB_BREAKPOINT_RAMP_DURATION');
  const holdDuration = args.holdDuration ?? readEnvDuration('NGB_BREAKPOINT_HOLD_DURATION');
  const rampDownDuration = args.rampDownDuration ?? readEnvDuration('NGB_BREAKPOINT_RAMP_DOWN_DURATION');

  if (rates) {
    stageArgs.rates = rates;
  }

  if (rampDuration) {
    stageArgs.rampDuration = rampDuration;
  }

  if (holdDuration) {
    stageArgs.holdDuration = holdDuration;
  }

  if (rampDownDuration) {
    stageArgs.rampDownDuration = rampDownDuration;
  }

  return buildBreakpointStages(stageArgs);
}

function validateStages(stages: readonly BreakpointStage[]): BreakpointStage[] {
  if (stages.length === 0) {
    throw new Error('Breakpoint profile requires at least one stage.');
  }

  return stages.map((stage, index) => ({
    duration: normalizeDuration(stage.duration, `stages[${index}].duration`),
    target: normalizeRate(stage.target, `stages[${index}].target`),
  }));
}

function normalizeRates(rates: readonly number[]): number[] {
  if (rates.length === 0) {
    throw new Error('Breakpoint profile requires at least one arrival-rate target.');
  }

  return rates.map((rate, index) => normalizeRate(rate, `rates[${index}]`));
}

function normalizeRate(value: number, name: string): number {
  if (!Number.isFinite(value) || value < 0) {
    throw new Error(`Expected ${name} to be a non-negative arrival-rate target but received: ${value}`);
  }

  return value;
}

function normalizeDuration(value: string, name: string): string {
  const normalized = value.trim();
  if (!normalized) {
    throw new Error(`Expected ${name} to be a non-empty k6 duration.`);
  }

  return normalized;
}

function readBreakpointRates(value: string | undefined): number[] | undefined {
  const normalized = value?.trim();
  if (!normalized) {
    return undefined;
  }

  const rates = normalized
    .split(/[,\s]+/)
    .filter(Boolean)
    .map((item) => Number(item));

  return normalizeRates(rates);
}

function resolvePositiveInteger(name: string, fallback: number, override?: number): number {
  const raw = __ENV[name]?.trim();
  const parsed = override ?? (raw ? Number(raw) : fallback);
  if (!Number.isInteger(parsed) || parsed <= 0) {
    throw new Error(`Expected ${name} to be a positive integer but received: ${override ?? raw}`);
  }

  return parsed;
}

function readEnvDuration(name: string): string | undefined {
  const value = __ENV[name]?.trim();
  return value ? value : undefined;
}
