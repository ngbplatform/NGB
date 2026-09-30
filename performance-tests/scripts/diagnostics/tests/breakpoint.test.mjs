import test, { beforeEach, afterEach } from 'node:test';
import assert from 'node:assert/strict';
import { buildBreakpointProfile } from '../../../ngb-performance-tests-framework/src/profiles/breakpoint.ts';

// These modules only build options; no k6 runtime, HTTP calls or database is used.
beforeEach(() => { globalThis.__ENV = {}; });
afterEach(() => { delete globalThis.__ENV; });

test('breakpoint allocates its entire default pool before the unchanged arrival schedule', () => {
  const options = buildBreakpointProfile();
  const scenario = options.scenarios.breakpoint;
  assert.equal(scenario.executor, 'ramping-arrival-rate');
  assert.equal(scenario.preAllocatedVUs, 500);
  assert.equal(scenario.maxVUs, 500);
  assert.equal(scenario.gracefulStop, '75s');
  assert.equal(scenario.startRate, 1);
  assert.equal(scenario.timeUnit, '1s');
  assert.equal(scenario.env.NGB_AUTH_INITIAL_JITTER_SECONDS, '45');
  assert.deepEqual(scenario.stages, [
    { duration: '2m', target: 2 }, { duration: '5m', target: 2 },
    { duration: '2m', target: 4 }, { duration: '5m', target: 4 },
    { duration: '2m', target: 8 }, { duration: '5m', target: 8 },
    { duration: '2m', target: 12 }, { duration: '5m', target: 12 },
    { duration: '2m', target: 16 }, { duration: '5m', target: 16 },
    { duration: '2m', target: 24 }, { duration: '5m', target: 24 },
    { duration: '2m', target: 32 }, { duration: '5m', target: 32 },
    { duration: '3m', target: 0 },
  ]);
  assert.deepEqual(options.thresholds.dropped_iterations, ['count<1']);
  assert.deepEqual(options.thresholds['http_req_failed{area:report-export}'], ['rate==0']);
  assert.deepEqual(options.thresholds['http_req_failed{operation:platform.documents.post}'], ['rate==0']);
});

test('explicit environment overrides change allocation and completion without relaxing the drop gate', () => {
  globalThis.__ENV = {
    NGB_BREAKPOINT_PRE_ALLOCATED_VUS: ' 600 ',
    NGB_BREAKPOINT_MAX_VUS: '700',
    NGB_BREAKPOINT_GRACEFUL_STOP: ' 2m ',
  };
  const options = buildBreakpointProfile();
  assert.equal(options.scenarios.breakpoint.preAllocatedVUs, 600);
  assert.equal(options.scenarios.breakpoint.maxVUs, 700);
  assert.equal(options.scenarios.breakpoint.gracefulStop, '2m');
  assert.deepEqual(options.thresholds.dropped_iterations, ['count<1']);
});

test('profile arguments take precedence over environment overrides', () => {
  globalThis.__ENV = {
    NGB_BREAKPOINT_PRE_ALLOCATED_VUS: 'invalid',
    NGB_BREAKPOINT_MAX_VUS: '-1',
    NGB_BREAKPOINT_GRACEFUL_STOP: '2m',
  };
  const options = buildBreakpointProfile({
    scenarioName: 'calibration', exec: 'calibrate',
    preAllocatedVUs: 20, maxVUs: 20, gracefulStop: '90s',
    stages: [{ duration: '10s', target: 1 }, { duration: '10s', target: 0 }],
  });
  const scenario = options.scenarios.calibration;
  assert.equal(scenario.preAllocatedVUs, 20);
  assert.equal(scenario.maxVUs, 20);
  assert.equal(scenario.gracefulStop, '90s');
  assert.equal(scenario.exec, 'calibrate');
  assert.deepEqual(scenario.stages, [{ duration: '10s', target: 1 }, { duration: '10s', target: 0 }]);
});

test('both argument and environment VU limits reject non-positive or non-integer values', () => {
  for (const [arg, env] of [
    ['preAllocatedVUs', 'NGB_BREAKPOINT_PRE_ALLOCATED_VUS'],
    ['maxVUs', 'NGB_BREAKPOINT_MAX_VUS'],
  ]) {
    for (const value of [0, -1, 1.5, NaN, Infinity]) {
      globalThis.__ENV = {};
      assert.throws(() => buildBreakpointProfile({ [arg]: value }), /positive integer/);
      globalThis.__ENV = { [env]: String(value) };
      assert.throws(() => buildBreakpointProfile(), /positive integer/);
    }
  }
});

test('maxVUs cannot be lower than preallocation, including the new default', () => {
  assert.throws(() => buildBreakpointProfile({ preAllocatedVUs: 60, maxVUs: 50 }), /maxVUs.*>= preAllocatedVUs/);
  globalThis.__ENV = { NGB_BREAKPOINT_MAX_VUS: '499' };
  assert.throws(() => buildBreakpointProfile(), /maxVUs.*>= preAllocatedVUs/);
});

test('blank environment settings retain defaults and explicit blank gracefulStop is rejected', () => {
  globalThis.__ENV = {
    NGB_BREAKPOINT_PRE_ALLOCATED_VUS: ' ',
    NGB_BREAKPOINT_MAX_VUS: '',
    NGB_BREAKPOINT_GRACEFUL_STOP: ' ',
  };
  const scenario = buildBreakpointProfile().scenarios.breakpoint;
  assert.equal(scenario.preAllocatedVUs, 500);
  assert.equal(scenario.maxVUs, 500);
  assert.equal(scenario.gracefulStop, '75s');
  assert.throws(() => buildBreakpointProfile({ gracefulStop: ' ' }), /gracefulStop/);
});
