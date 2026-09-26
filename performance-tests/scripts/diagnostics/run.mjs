import { spawn } from 'node:child_process';
import { pathToFileURL } from 'node:url';
import { randomUUID } from 'node:crypto';
import { mkdirSync, writeFileSync, appendFileSync, createWriteStream, readFileSync, statSync, renameSync } from 'node:fs';
import { resolve, join } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { finished } from 'node:stream/promises';
import { parseOptions, workspace, loadEnvironment, workloadEnvironment, databaseEnvironment } from './config.mjs';
import { Monitor, command, inspectContainers } from './monitor.mjs';

function hasCompleteOutput(directory) {
  try {
    const summary = JSON.parse(readFileSync(join(directory, 'summary.json'), 'utf8'));
    const manifest = JSON.parse(readFileSync(join(directory, 'summary.manifest.json'), 'utf8'));
    return Number.isFinite(summary.metrics?.http_reqs?.values?.count)
      && summary.metrics.http_reqs.values.count > 0 && Boolean(summary.runConfiguration?.scenarios)
      && Boolean(manifest.testFileSha256) && statSync(join(directory, 'samples.json.gz')).size > 0;
  } catch { return false; }
}

export async function runDiagnostic(options, dependencies = {}) {
  const root = dependencies.workspace || workspace;
  const executeCommand = dependencies.command || command;
  const inspect = dependencies.inspectContainers || inspectContainers;
  const launch = dependencies.spawn || spawn;
  if (options.help) {
    console.log(`Usage: node scripts/diagnostics/run.mjs --profile <profile> [options]
  Profiles: write-heavy, platform-mixed-capacity, platform-read-capacity, platform-breakpoint
    --env-file <path>           Workload env file; paths are relative to the workspace
    --database-env-file <path>  PostgreSQL env file (default: ../.env.pm)
    --dataset-id <id>           Identity of the restored dataset, when known
    --check                    Read-only preflight; do not launch k6
  Requires macOS/Linux, Node >=22.13, Docker, k6, PostgreSQL >=17 and installed npm dependencies.
  Results: artifacts/runs/<profile>-<UTC>-<unique-id>/
  Mixed capacity and write-heavy enable writes and fresh posting. Period close stays disabled.`);
    return 0;
  }

  const writes = ['write-heavy', 'platform-mixed-capacity'].includes(options.profile);
  const envFile = resolve(root, options['env-file'] || `ngb-property-management-perf/.env.${writes ? 'write.' : ''}local`);
  const env = workloadEnvironment(options.profile, loadEnvironment(envFile));
  const dbEnv = databaseEnvironment(resolve(root, options['database-env-file']));
  const containers = [env.NGB_DIAGNOSTICS_API_CONTAINER || 'ngb.pm.api', env.NGB_DIAGNOSTICS_DB_CONTAINER || 'ngb.pm.postgres'];
  const monitor = dependencies.monitor || new Monitor(dbEnv, containers);
  const stamp = new Date().toISOString().replaceAll(/[-:]/g, '').replace(/\.\d+Z$/, 'Z');
  const directory = join(root, 'artifacts', 'runs', `${options.profile}-${stamp}-${randomUUID().slice(0, 8)}`);
  mkdirSync(directory, { recursive: true, mode: 0o700 });
  const writeJson = (name, value) => writeFileSync(join(directory, name), `${JSON.stringify(value, null, 2)}\n`, { mode: 0o600 });
  const state = { schemaVersion: 1, profile: options.profile, status: 'preflight', createdAtUtc: new Date().toISOString(),
    startedAtUtc: null, completedAtUtc: null, exitCode: null, k6ExitCode: null, signal: null,
    diagnosticErrorSamples: 0, outputDirectory: directory, failureReason: null };
  const persist = () => { writeJson('run.json.tmp', state); renameSync(join(directory, 'run.json.tmp'), join(directory, 'run.json')); };
  const resources = sample => {
    appendFileSync(join(directory, 'resources.jsonl'), `${JSON.stringify(sample)}\n`, { mode: 0o600 });
    if (Object.values(sample).some(value => value?.error)) state.diagnosticErrorSamples++;
  };
  persist();
  let child;
  let stopping = false;
  let polling;
  let killTimer;
  let log;
  let completion;
  let childClosed;
  let workloadClosed = false;
  const cancelled = new AbortController();
  const stopChild = signal => {
    if (!child?.pid || workloadClosed) return;
    try { process.kill(-child.pid, signal); } catch (error) { if (error.code !== 'ESRCH') throw error; }
  };
  const onSignal = signal => {
    state.signal ||= signal;
    stopping = true;
    cancelled.abort();
    stopChild(signal);
    if (!killTimer && child) killTimer = setTimeout(() => stopChild('SIGKILL'), 10000);
  };
  process.on('SIGINT', onSignal);
  process.on('SIGTERM', onSignal);

  try {
    if (process.platform === 'win32') throw new Error('Diagnostic runner requires macOS or Linux; use run-k6.ps1 for ordinary Windows runs.');
    await executeCommand('k6', ['version']); // Verify availability only; never runs a test.
    const containerMetadata = await inspect(containers);
    await monitor.connect();
    const first = await monitor.sample();
    resources(first);
    if (state.diagnosticErrorSamples) throw new Error('Read-only diagnostic preflight failed; inspect resources.jsonl and database configuration.');
    if (first.load_generator.length) throw new Error('A k6 process is already running. Finish it before starting another diagnostic run.');
    writeJson('environment.json', {
      schemaVersion: 1, containers: containerMetadata,
      postgres: { host: dbEnv.PGHOST, port: Number(dbEnv.PGPORT), database: dbEnv.PGDATABASE },
      datasetId: options['dataset-id'] || env.NGB_PERF_DATASET_ID || null,
      datasetRestoredByRunner: false, samplingIntervalSeconds: 5, containerIntervalSeconds: 15,
      transactionSampleLimit: 200, note: 'CPU 100% represents approximately one core. Sampling can miss brief waits.',
    });
    console.log(`Results and diagnostics: ${directory}`);
    if (state.signal) throw new Error('Preflight interrupted.');
    if (options.check) {
      state.status = 'preflight-passed';
      state.exitCode = 0;
      console.log('Read-only preflight passed. No load was generated.');
    } else {
      env.NGB_PERF_API_IMAGE_ID = containerMetadata[0].image;
      env.NGB_PERF_DATASET_ID = options['dataset-id'] || env.NGB_PERF_DATASET_ID || 'unrestored-current-local-data';
      env.NGB_K6_TIME_SERIES_EXPORT = join(directory, 'samples.json.gz');
      state.status = 'running';
      state.startedAtUtc = new Date().toISOString();
      persist();
      log = createWriteStream(join(directory, 'run.log'), { flags: 'wx', mode: 0o600 });
      child = launch('bash', [join(root, 'scripts/run-k6.sh'), '--env-file', envFile,
        '--test', `ngb-property-management-perf/src/tests/${options.profile}.ts`,
        '--summary-export', join(directory, 'summary.json')],
      { cwd: root, env, detached: true, stdio: ['ignore', 'pipe', 'pipe'] });
      child.stdout.pipe(log, { end: false });
      child.stderr.pipe(log, { end: false });
      child.stdout.pipe(process.stdout, { end: false });
      child.stderr.pipe(process.stderr, { end: false });
      childClosed = new Promise(resolveChild => {
        child.once('error', () => { workloadClosed = true; resolveChild({ code: 2, signal: null }); });
        child.once('close', (code, signal) => { workloadClosed = true; resolveChild({ code, signal }); });
      });
      completion = Promise.race([childClosed, new Promise((_, reject) => log.once('error', reject))]);
      polling = (async () => {
        let iteration = 1;
        while (!stopping) {
          try { await delay(5000, undefined, { signal: cancelled.signal }); } catch { break; }
          if (stopping) break;
          resources(await monitor.sample(iteration++ % 3 === 0));
        }
      })();
      // Surface sampler failures immediately rather than allowing unhandled rejections.
      polling.catch(() => {
        state.diagnosticErrorSamples++;
        stopChild('SIGTERM');
        if (!killTimer) killTimer = setTimeout(() => stopChild('SIGKILL'), 10000);
      });
      const result = await completion;
      state.k6ExitCode = result.code;
      state.signal ||= result.signal;
      stopping = true;
      cancelled.abort();
      await polling;
      resources(await monitor.sample());
      state.exitCode = state.signal ? (state.signal === 'SIGINT' ? 130 : 143) : (result.code ?? 1);
      if (!state.exitCode && (state.diagnosticErrorSamples || !hasCompleteOutput(directory))) {
        state.exitCode = 2;
        state.failureReason = 'Diagnostic gaps or incomplete summary, manifest or time-series output.';
      }
      state.status = state.signal ? 'interrupted' : state.exitCode === 0 ? 'completed' : 'failed';
    }
  } catch (error) {
    stopChild('SIGTERM');
    if (child && !killTimer) killTimer = setTimeout(() => stopChild('SIGKILL'), 10000);
    state.status = state.signal ? 'interrupted' : 'failed';
    state.exitCode = state.signal === 'SIGINT' ? 130 : state.signal ? 143 : 2;
    // Known operator errors are actionable; subprocess/driver details can contain secrets.
    state.failureReason = error.code || error.cmd ? 'Diagnostic command failed. Check tool availability, Docker and local configuration.' : error.message;
    console.error(state.failureReason);
  } finally {
    stopping = true;
    cancelled.abort();
    if (childClosed) await childClosed;
    if (killTimer) clearTimeout(killTimer);
    if (polling) await polling.catch(() => {});
    if (log) { log.end(); await finished(log).catch(() => {}); }
    await monitor.close().catch(() => {});
    state.completedAtUtc = new Date().toISOString();
    persist();
    process.removeListener('SIGINT', onSignal);
    process.removeListener('SIGTERM', onSignal);
  }
  return state.exitCode ?? 2;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.umask(0o077);
  try { process.exitCode = await runDiagnostic(parseOptions(process.argv.slice(2))); }
  catch { console.error('Invalid or unreadable configuration. Check --help and local env files.'); process.exitCode = 2; }
}
