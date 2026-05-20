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

export interface CapacityStage {
  readonly duration: string;
  readonly target: number;
}

export interface CapacityProfileArgs extends SingleScenarioProfileArgs {
  readonly stages?: readonly CapacityStage[];
  readonly targets?: readonly number[];
  readonly rampDuration?: string;
  readonly holdDuration?: string;
  readonly rampDownDuration?: string;
  readonly gracefulRampDown?: string;
}

const DEFAULT_CAPACITY_TARGETS = [80, 160, 240, 320] as const;
const DEFAULT_RAMP_DURATION = '5m';
const DEFAULT_HOLD_DURATION = '10m';
const DEFAULT_RAMP_DOWN_DURATION = '5m';
const DEFAULT_GRACEFUL_RAMP_DOWN = '1m';

export function buildCapacityProfile(args: CapacityProfileArgs = {}): Options {
  const scenarioName = args.scenarioName ?? 'capacity';

  return withSummaryTrendStats({
    scenarios: {
      [scenarioName]: {
        executor: 'ramping-vus',
        stages: [...resolveCapacityStages(args)],
        gracefulRampDown: args.gracefulRampDown ?? DEFAULT_GRACEFUL_RAMP_DOWN,
        exec: args.exec ?? 'default',
        ...(args.env ? { env: args.env } : {}),
        tags: { profile: 'capacity', ...(args.tags ?? {}) },
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

export function buildCapacityStages(args: {
  readonly targets?: readonly number[];
  readonly rampDuration?: string;
  readonly holdDuration?: string;
  readonly rampDownDuration?: string;
} = {}): CapacityStage[] {
  const targets = normalizeTargets(args.targets ?? DEFAULT_CAPACITY_TARGETS);
  const rampDuration = normalizeDuration(args.rampDuration ?? DEFAULT_RAMP_DURATION, 'rampDuration');
  const holdDuration = normalizeDuration(args.holdDuration ?? DEFAULT_HOLD_DURATION, 'holdDuration');
  const rampDownDuration = normalizeDuration(
    args.rampDownDuration ?? DEFAULT_RAMP_DOWN_DURATION,
    'rampDownDuration',
  );

  return [
    ...targets.flatMap((target) => [
      { duration: rampDuration, target },
      { duration: holdDuration, target },
    ]),
    { duration: rampDownDuration, target: 0 },
  ];
}

function resolveCapacityStages(args: CapacityProfileArgs): CapacityStage[] {
  if (args.stages) {
    return validateStages(args.stages);
  }

  const stageArgs: {
    targets?: readonly number[];
    rampDuration?: string;
    holdDuration?: string;
    rampDownDuration?: string;
  } = {};
  const targets = args.targets ?? readCapacityTargets(__ENV.NGB_CAPACITY_VUS);
  const rampDuration = args.rampDuration ?? readEnvDuration('NGB_CAPACITY_RAMP_DURATION');
  const holdDuration = args.holdDuration ?? readEnvDuration('NGB_CAPACITY_HOLD_DURATION');
  const rampDownDuration = args.rampDownDuration ?? readEnvDuration('NGB_CAPACITY_RAMP_DOWN_DURATION');

  if (targets) {
    stageArgs.targets = targets;
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

  return buildCapacityStages(stageArgs);
}

function validateStages(stages: readonly CapacityStage[]): CapacityStage[] {
  if (stages.length === 0) {
    throw new Error('Capacity profile requires at least one stage.');
  }

  return stages.map((stage, index) => ({
    duration: normalizeDuration(stage.duration, `stages[${index}].duration`),
    target: normalizeTarget(stage.target, `stages[${index}].target`),
  }));
}

function normalizeTargets(targets: readonly number[]): number[] {
  if (targets.length === 0) {
    throw new Error('Capacity profile requires at least one VU target.');
  }

  return targets.map((target, index) => normalizeTarget(target, `targets[${index}]`));
}

function normalizeTarget(value: number, name: string): number {
  if (!Number.isInteger(value) || value < 0) {
    throw new Error(`Expected ${name} to be a non-negative integer VU target but received: ${value}`);
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

function readCapacityTargets(value: string | undefined): number[] | undefined {
  const normalized = value?.trim();
  if (!normalized) {
    return undefined;
  }

  const targets = normalized
    .split(/[,\s]+/)
    .filter(Boolean)
    .map((item) => Number(item));

  return normalizeTargets(targets);
}

function readEnvDuration(name: string): string | undefined {
  const value = __ENV[name]?.trim();
  return value ? value : undefined;
}
