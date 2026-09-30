import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { contentionConfig } from '../contention-config.mjs';
import { workloadEnvironment } from '../config.mjs';
import { ContentionController, consumeLines, parseJobLog } from '../contention.mjs';

test('contention defaults are 320 VUs, 30 minutes, trigger at minute 10', () => {
  const config = contentionConfig({});
  assert.equal(config.mode, 'overlap');
  assert.equal(config.vus, 320);
  assert.equal(config.durationSeconds, 1800);
  assert.equal(config.triggerSeconds, 600);
  assert.equal(contentionConfig({ NGB_CONTENTION_MODE: 'reports' }).vus, 800);
  const env = workloadEnvironment('platform-contention', {});
  assert.equal(env.NGB_CAPACITY_VUS, '320');
  assert.equal(env.NGB_CAPACITY_HOLD_DURATION, '20m');
  assert.equal(env.NGB_PERF_ENABLE_POSTING, 'true');
  assert.equal(env.NGB_PM_POSTING_MODE, 'fresh');
});

test('ambiguous workloads and triggers outside the hold are rejected before mutation', () => {
  for (const NGB_CAPACITY_VUS of ['320,480', '0', '-1', '1.5', 'Infinity', '5001']) {
    assert.throws(() => contentionConfig({ NGB_CAPACITY_VUS }));
  }
  for (const NGB_CONTENTION_TRIGGER_AFTER of ['0s', '5m', '25m', '30m', '600', 'NaN', '1d']) {
    assert.throws(() => contentionConfig({ NGB_CONTENTION_TRIGGER_AFTER }));
  }
  assert.throws(() => contentionConfig({ NGB_CONTENTION_MODE: 'unknown' }));
  assert.throws(() => contentionConfig({ NGB_CAPACITY_HOLD_DURATION: '0s' }));
});

test('split process output is reconstructed with a bounded buffer', () => {
  const lines = [];
  const read = consumeLines(line => lines.push(line), 10);
  read('a'); read('bc\nd'); read('ef\n');
  assert.deepEqual(lines, ['abc', 'def']);
  assert.throws(() => read('x'.repeat(11)));
});

test('job logs expose only finalization evidence, not raw application output', () => {
  const prefix = '2026-09-27T20:00:00Z [INF] () ["opreg.finalization.run_dirty_months"] ';
  assert.deepEqual(parseJobLog(prefix + 'START at 2026-09-27T20:00:00Z'),
    { kind: 'finalization-start', at: '2026-09-27T20:00:00Z' });
  assert.deepEqual(parseJobLog(prefix + 'OK. FinalizedCount=6. MaxItems=50. DurationMs=157000'),
    { kind: 'finalization-finished', at: '2026-09-27T20:00:00Z', finalized: 6, durationMs: 157000 });
  assert.equal(parseJobLog('password=never-write-this'), null);
  assert.equal(parseJobLog(prefix + 'Exception sensitive detail'), null);
  assert.equal(parseJobLog(prefix + 'JobRunSummary Outcome="SkippedOverlap"').outcome, 'SkippedOverlap');
});

async function controllerTest(action, mode = 'overlap') {
  const directory = mkdtempSync(join(tmpdir(), 'ngb-contention-'));
  const snapshot = { at: new Date().toISOString(), last_job_id: '10',
    months: [{ code: 'pm.receivables_open_items', status: 2 }], job: { id: '10', state: 'Succeeded' } };
  const calls = [];
  const controller = new ContentionController({ root: directory, directory,
    env: { NGB_CONTENTION_MODE: mode }, monitor: { sampleContention: async () => structuredClone(snapshot) },
    execute: async (...args) => { calls.push(args); return JSON.stringify({ kind: 'triggered', id: '11', at: new Date().toISOString() }); } });
  controller.initialJob = '10';
  controller.origin = Date.now() - 601000;
  controller.lastCollection = Date.now();
  try { await action(controller, snapshot, calls, directory); }
  finally { rmSync(directory, { recursive: true, force: true }); }
}

test('controlled trigger happens exactly once and targets the deployed Hangfire worker', async () => {
  await controllerTest(async (controller, snapshot, calls, directory) => {
    await controller.tick();
    assert.equal(calls.length, 1);
    assert.deepEqual(calls[0][1].slice(0, 2), ['exec', 'ngb.pm.backgroundjobs']);
    assert.equal(calls[0][1].at(-1), 'trigger');
    snapshot.last_job_id = '11'; snapshot.job = { id: '11', state: 'Processing' };
    await controller.tick();
    assert.equal(calls.length, 1);
    const events = readFileSync(join(directory, 'contention.jsonl'), 'utf8');
    assert.match(events, /trigger-attempt/);
    assert.match(events, /triggered/);
  });
});

for (const mode of ['control', 'reports']) test(`${mode} mode never triggers a job`, async () => {
  await controllerTest(async (controller, _snapshot, calls) => { await controller.tick(); assert.equal(calls.length, 0); }, mode);
});

test('missing dirty work prevents enqueue', async () => {
  await controllerTest(async (controller, snapshot, calls) => {
    snapshot.months[0].status = 1;
    await assert.rejects(controller.tick(), /No dirty/);
    assert.equal(calls.length, 0);
  });
});

test('foreign runs, stale telemetry and failed jobs fail the experiment', async () => {
  await controllerTest(async (controller, snapshot, calls) => {
    snapshot.last_job_id = '12';
    await assert.rejects(controller.tick(), /Another finalization/);
    assert.equal(calls.length, 0);
    snapshot.last_job_id = '10'; controller.lastCollection = Date.now() - 20000;
    await assert.rejects(controller.tick(), /telemetry has stopped/);
    controller.lastCollection = Date.now(); controller.triggered = { id: '11' };
    snapshot.last_job_id = '11'; snapshot.job = { id: '11', state: 'Failed' };
    await assert.rejects(controller.tick(), /did not succeed/);
  });
});

test('an ambiguous enqueue is never retried', async () => {
  await controllerTest(async (controller, _snapshot, calls) => {
    controller.execute = async () => { calls.push('trigger'); throw new Error('transport failed'); };
    await assert.rejects(controller.tick());
    await controller.tick();
    assert.equal(calls.length, 1);
    await assert.rejects(controller.finish(), /never triggered/);
  });
});

test('late trigger and unfinished job cannot silently pass', async () => {
  await controllerTest(async (controller, snapshot, calls) => {
    controller.origin = Date.now() - 1600000;
    await assert.rejects(controller.tick(), /Missed/);
    assert.equal(calls.length, 0);
    controller.triggered = { id: '11' }; controller.triggerAttempted = true;
    snapshot.last_job_id = '11'; snapshot.job = { id: '11', state: 'Processing' };
    await assert.rejects(controller.tick(), /did not finish/);
  });
});

test('cancellation during a database sample prevents a later enqueue', async () => {
  await controllerTest(async (controller, snapshot, calls) => {
    controller.monitor.sampleContention = async () => { controller.stopTriggering(); return snapshot; };
    await controller.tick();
    assert.equal(calls.length, 0);
    await assert.rejects(controller.finish(), /never triggered/);
  });
});
