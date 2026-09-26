import test from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, readdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { runDiagnostic } from '../run.mjs';

async function withWorkspace(action) {
  const workspace = mkdtempSync(join(tmpdir(), 'ngb empty artifacts '));
  const directory = join(workspace, 'ngb-property-management-perf');
  mkdirSync(directory);
  writeFileSync(join(directory, '.env.write.local'), '');
  writeFileSync(join(workspace, 'database.env'), 'PGDATABASE=test\nPGUSER=test\nPGPASSWORD=local-test\n');
  const sample = { at: '2026-01-01T00:00:00Z', postgres_mcp: { rows: [{ sample: { read_only: 'on' } }] },
    containers: [], load_generator: [] };
  let closed = false;
  const dependencies = { workspace, monitor: { connect: async () => {}, sample: async () => sample,
    close: async () => { closed = true; } },
    command: async (name, args) => { assert.equal(name, 'k6'); assert.deepEqual(args, ['version']); return 'fake version'; },
    inspectContainers: async () => [{ image: 'fake-api' }, { image: 'fake-db' }],
    spawn: () => { throw new Error('Preflight must never launch a workload.'); } };
  const options = { profile: 'platform-mixed-capacity', 'database-env-file': 'database.env', check: true };
  const result = () => {
    const root = join(workspace, 'artifacts/runs');
    const output = join(root, readdirSync(root)[0]);
    return { state: JSON.parse(readFileSync(join(output, 'run.json'))), output };
  };
  try { await action({ dependencies, options, sample, result, isClosed: () => closed }); }
  finally { rmSync(workspace, { recursive: true, force: true }); }
}

test('read-only preflight works with no artifacts directory and never launches load', async () => {
  await withWorkspace(async ({ dependencies, options, result, isClosed }) => {
    assert.equal(await runDiagnostic(options, dependencies), 0);
    assert.equal(result().state.status, 'preflight-passed');
    assert.equal(result().state.k6ExitCode, null);
    assert.equal(isClosed(), true);
  });
});

test('failed preflight blocks load and records diagnostic failure', async () => {
  await withWorkspace(async ({ dependencies, options, sample, result }) => {
    sample.postgres_mcp = { error: 'unavailable' };
    options.check = false;
    assert.equal(await runDiagnostic(options, dependencies), 2);
    assert.equal(result().state.status, 'failed');
    assert.equal(result().state.diagnosticErrorSamples, 1);
  });
});

test('existing k6 process blocks a second workload', async () => {
  await withWorkspace(async ({ dependencies, options, sample, result }) => {
    sample.load_generator = [{ pid: 123 }];
    options.check = false;
    assert.equal(await runDiagnostic(options, dependencies), 2);
    assert.equal(result().state.status, 'failed');
  });
});

function fakeWorkload(code, summary = true) {
  return (name, args, options) => {
    assert.equal(name, 'bash');
    assert.equal(options.env.NGB_PERF_ENABLE_WRITES, 'true');
    assert.equal(options.env.NGB_PERF_ENABLE_POSTING, 'true');
    assert.equal(options.env.NGB_PM_POSTING_MODE, 'fresh');
    const script = `const fs = require('fs'); const path = require('path'); const zlib = require('zlib');
      if (process.argv[2] === 'yes') {
        fs.writeFileSync(process.argv[1], JSON.stringify({metrics:{http_reqs:{values:{count:1}}},runConfiguration:{scenarios:{}}}));
        fs.writeFileSync(path.join(path.dirname(process.argv[1]), 'summary.manifest.json'), JSON.stringify({testFileSha256:'test'}));
        fs.writeFileSync(path.join(path.dirname(process.argv[1]), 'samples.json.gz'), zlib.gzipSync('synthetic sample'));
      }
      process.exit(${code});`;
    return spawn(process.execPath, ['-e', script, args.at(-1), summary ? 'yes' : 'no'], options);
  };
}

test('workload failure preserves exit 99 and drains the monitor', async () => {
  await withWorkspace(async ({ dependencies, options, result, isClosed }) => {
    dependencies.spawn = fakeWorkload(99);
    options.check = false;
    assert.equal(await runDiagnostic(options, dependencies), 99);
    assert.equal(result().state.k6ExitCode, 99);
    assert.equal(result().state.status, 'failed');
    assert.equal(isClosed(), true);
  });
});

test('successful workload without a summary is not a complete diagnostic run', async () => {
  await withWorkspace(async ({ dependencies, options, result }) => {
    dependencies.spawn = fakeWorkload(0, false);
    options.check = false;
    assert.equal(await runDiagnostic(options, dependencies), 2);
    assert.equal(result().state.k6ExitCode, 0);
    assert.equal(result().state.status, 'failed');
  });
});

test('successful complete run records completion and closes monitor', async () => {
  await withWorkspace(async ({ dependencies, options, result, isClosed }) => {
    dependencies.spawn = fakeWorkload(0);
    options.check = false;
    assert.equal(await runDiagnostic(options, dependencies), 0);
    assert.equal(result().state.status, 'completed');
    assert.equal(isClosed(), true);
  });
});

test('SIGINT reaches the workload and records interruption rather than success', async () => {
  await withWorkspace(async ({ dependencies, options, result, isClosed }) => {
    dependencies.spawn = (_name, _args, spawnOptions) => {
      const child = spawn(process.execPath, ['-e', "process.on('SIGINT', () => process.exit(130)); console.log('ready'); setInterval(() => {}, 1000);"], spawnOptions);
      child.stdout.once('data', () => process.emit('SIGINT', 'SIGINT'));
      return child;
    };
    options.check = false;
    assert.equal(await runDiagnostic(options, dependencies), 130);
    assert.equal(result().state.status, 'interrupted');
    assert.equal(result().state.signal, 'SIGINT');
    assert.equal(isClosed(), true);
  });
});
