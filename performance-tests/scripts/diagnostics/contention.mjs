import { spawn } from 'node:child_process';
import { appendFileSync, writeFileSync } from 'node:fs';
import { join, basename } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { contentionConfig } from './contention-config.mjs';
import { command } from './monitor.mjs';

export function consumeLines(consume, maxLength = 65536) {
  let pending = '';
  return chunk => {
    pending += String(chunk);
    let end;
    while ((end = pending.indexOf('\n')) >= 0) {
      const line = pending.slice(0, end); pending = pending.slice(end + 1);
      if (line.length > maxLength) throw new Error('Diagnostic line exceeds its bound.');
      consume(line);
    }
    if (pending.length > maxLength) throw new Error('Diagnostic line exceeds its bound.');
  };
}

export function parseJobLog(line) {
  if (!line.includes('opreg.finalization.run_dirty_months')) return null;
  const start = line.match(/START at (\S+)/);
  if (start && Number.isFinite(Date.parse(start[1]))) return { kind: 'finalization-start', at: start[1] };
  const ok = line.match(/OK\. FinalizedCount=(\d+)\. MaxItems=(\d+)\. DurationMs=(\d+)/);
  const at = line.match(/^(\S+)/)?.[1];
  if (ok && Number.isFinite(Date.parse(at))) return { kind: 'finalization-finished', at, finalized: Number(ok[1]), durationMs: Number(ok[3]) };
  if (/JobRunSummary/.test(line)) {
    const outcome = line.match(/Outcome="?(\w+)/)?.[1];
    return { kind: 'finalization-summary', at, outcome };
  }
  return null;
}

export class ContentionController {
  constructor({ root, directory, env, monitor, execute = command, launch = spawn }) {
    Object.assign(this, { root, directory, env, monitor, execute, launch });
    this.config = contentionConfig(env);
    this.api = env.NGB_DIAGNOSTICS_API_CONTAINER || 'ngb.pm.api';
    this.worker = env.NGB_DIAGNOSTICS_WORKER_CONTAINER || 'ngb.pm.backgroundjobs';
    this.remote = `/tmp/ngb-perf-${basename(directory)}`;
    this.children = [];
    this.copied = [];
    this.instruments = new Set();
    this.error = null;
    this.origin = null;
    this.triggered = null;
    this.triggerAttempted = false;
    this.stopping = false;
    this.lastCollection = null;
    this.output = consumeLines(line => {
      const match = line.match(/NGB_CONTENTION_ORIGIN=(\d+)/);
      if (match && this.origin === null) {
        this.origin = Number(match[1]);
        this.event({ kind: 'origin', at: new Date(this.origin).toISOString() });
      }
    });
  }

  event(value) { appendFileSync(join(this.directory, 'contention.jsonl'), `${JSON.stringify(value)}\n`, { mode: 0o600 }); }
  assertHealthy() { if (this.error) throw new Error(this.error); }
  async step(phase, executable, args, options) {
    this.event({ kind: 'setup-step', phase, at: new Date().toISOString() });
    try { return await this.execute(executable, args, options); }
    catch (error) {
      let reason = '';
      try {
        const result = JSON.parse(error.stdout);
        if (result.kind === 'error' && /^[A-Za-z]+Exception$/.test(result.errorType)) reason = ` (${result.errorType})`;
      } catch { /* Do not expose untrusted subprocess output. */ }
      throw new Error(`Contention setup failed at ${phase}${reason}. Check container paths and .NET tool availability.`);
    }
  }
  startStream(args, onLine, input = false) {
    const child = this.launch('docker', args, { stdio: [input ? 'pipe' : 'ignore', 'pipe', 'pipe'] });
    child.stdin?.on('error', () => { if (!this.stopping) this.error ||= 'Diagnostic input stream failed.'; });
    const consume = consumeLines(onLine);
    const receive = chunk => { try { consume(chunk); } catch { this.error ||= 'Invalid diagnostic stream.'; } };
    child.stdout.on('data', receive);
    // docker logs may deliver application logs on either stream; collector stderr is never persisted.
    if (input) child.stderr.resume(); else child.stderr.on('data', receive);
    child.on('error', () => { this.error ||= 'Diagnostic subprocess failed.'; });
    const closed = new Promise(resolve => child.once('close', code => {
      if (!this.stopping) this.error ||= 'Diagnostic subprocess stopped unexpectedly.';
      resolve(code);
    }));
    this.children.push({ child, closed, input });
  }

  async prepare() {
    writeFileSync(join(this.directory, 'contention-config.json'), JSON.stringify(this.config, null, 2), { mode: 0o600 });
    const snapshot = await this.monitor.sampleContention();
    if (!snapshot.registered || snapshot.live_workers < 1) throw new Error('Finalization recurring job or live Hangfire worker is missing.');
    if (snapshot.job && !['Succeeded', 'Deleted', 'Failed'].includes(snapshot.job.state)) throw new Error('A finalization job is already active.');
    if (Math.abs(Date.now() - Date.parse(snapshot.at)) > 5000) throw new Error('Host/database clocks differ by more than 5 seconds.');
    this.initialJob = snapshot.last_job_id;
    this.event({ kind: 'snapshot', ...snapshot });
    const output = join(this.root, 'artifacts/tooling/contention-control');
    await this.step('build-helper', 'dotnet', ['publish', join(this.root, 'scripts/diagnostics/control/Ngb.Perf.Control.csproj'),
      '-c', 'Release', '--nologo', '-v', 'quiet', '-o', output], { timeout: 120000 });
    for (const container of [this.api, this.worker]) {
      await this.step(`copy-helper:${container}`, 'docker', ['cp', `${output}/.`, `${container}:${this.remote}`]);
      this.copied.push(container);
      // docker cp creates root-owned files and preserves the runner's private
      // umask. Only these non-secret binaries need to be readable by APP_UID.
      await this.step(`helper-permissions:${container}`, 'docker', ['exec', '--user', '0', container, 'chmod', '-R', 'a+rX', this.remote]);
    }
    const validation = JSON.parse(await this.step('validate-worker', 'docker', ['exec', this.worker, 'dotnet', `${this.remote}/Ngb.Perf.Control.dll`, 'validate-worker']));
    if (validation.kind !== 'worker-ready') throw new Error('Deployed finalization worker validation failed.');
    this.startStream(['logs', '--follow', '--since', new Date().toISOString(), this.worker], line => {
      const event = parseJobLog(line);
      if (event) this.event(event);
    });
    this.startStream(['exec', '-i', this.api, 'dotnet', `${this.remote}/Ngb.Perf.Control.dll`, 'meters'], line => {
      const event = JSON.parse(line);
      if (event.kind === 'error') this.error ||= 'Report admission metric collection failed.';
      if (event.eventName === 'GaugeValuePublished') this.lastCollection = Date.now();
      if (event.eventName === 'GaugeValuePublished') this.instruments.add(event.payload?.instrumentName);
      appendFileSync(join(this.directory, 'report-admission.jsonl'), `${JSON.stringify(event)}\n`, { mode: 0o600 });
    }, true);
    for (let attempt = 0; attempt < 100; attempt++) {
      this.assertHealthy();
      if (['active', 'queued'].every(name => this.instruments.has(`ngb.report.admission.${name}`))) return;
      await delay(200);
    }
    throw new Error('Report admission gauges were not received. Verify API image and .NET diagnostics availability.');
  }

  onOutput(chunk) { try { this.output(chunk); } catch { this.error ||= 'Cannot read the k6 start marker.'; } }
  stopTriggering() { this.triggeringStopped = true; }

  async tick() {
    this.assertHealthy();
    if (!this.lastCollection || Date.now() - this.lastCollection > 15000) throw new Error('Report admission telemetry has stopped.');
    const snapshot = await this.monitor.sampleContention(this.triggered?.id);
    this.event({ kind: 'snapshot', ...snapshot });
    const expectedJob = this.triggered?.id ?? this.initialJob;
    if (snapshot.last_job_id !== expectedJob) throw new Error('Another finalization run contaminated this experiment.');
    if (!this.origin) return;
    const elapsed = (Date.now() - this.origin) / 1000;
    if (this.config.mode === 'overlap' && !this.triggeringStopped && !this.triggerAttempted && elapsed >= this.config.triggerSeconds) {
      if (elapsed >= this.config.rampSeconds + this.config.holdSeconds) throw new Error('Missed the finalization trigger window.');
      if (!snapshot.months.some(month => month.code === 'pm.receivables_open_items' && month.status === 2)) {
        throw new Error('No dirty receivables month: the overlap experiment would be empty.');
      }
      this.triggerAttempted = true; // Never retry an ambiguous enqueue result.
      this.event({ kind: 'trigger-attempt', at: new Date().toISOString() });
      const result = JSON.parse(await this.execute('docker', ['exec', this.worker, 'dotnet', `${this.remote}/Ngb.Perf.Control.dll`, 'trigger'], { timeout: 60000 }));
      if (result.kind !== 'triggered' || !/^[1-9]\d{0,17}$/.test(result.id)) throw new Error('Finalization enqueue was not confirmed.');
      this.triggered = result;
      this.event(result);
      console.log(`Finalization enqueued in Hangfire: ${result.id}`);
    }
    if (snapshot.job?.id === this.triggered?.id && ['Failed', 'Deleted'].includes(snapshot.job?.state)) throw new Error('The triggered finalization job did not succeed.');
    if (this.config.mode === 'overlap' && elapsed >= this.config.rampSeconds + this.config.holdSeconds && snapshot.job?.state !== 'Succeeded') {
      throw new Error('Finalization did not finish during the fixed-load hold.');
    }
  }

  async finish() {
    await this.tick();
    if (!this.origin) throw new Error('Missing k6 scenario start marker.');
    if (this.config.mode === 'overlap' && !this.triggered) throw new Error('Finalization was never triggered.');
  }

  close() { return this.closing ||= this.stop(); }

  async stop() {
    this.stopping = true;
    for (const { child, input } of this.children) {
      if (input) child.stdin.end('\n'); else child.kill('SIGTERM');
    }
    for (const { child, closed } of this.children) {
      let timer;
      await Promise.race([closed, new Promise(resolve => { timer = setTimeout(() => { child.kill('SIGKILL'); resolve(); }, 5000); })]);
      clearTimeout(timer);
    }
    for (const container of this.copied) {
      // Only this run's unique temporary tool directory; no application data is removed.
      await this.execute('docker', ['exec', '--user', '0', container, 'rm', '-rf', this.remote]).catch(() => {});
    }
  }
}
