import assert from 'node:assert/strict'
import { readFile, writeFile } from 'node:fs/promises'
import { cpus, release, totalmem } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import profile from './load-profile.json' with { type: 'json' }
import { digest } from './kit-contracts.mjs'
import { loadSummary } from './performance.mjs'
import { runTool } from './process.mjs'

export async function runLoadProfile({ environment, output, env, signal }) {
  const checks = []
  const required = ['performance.toolchain', 'performance.identity',
    ...Array.from({ length: profile.repetitions }, (_, index) => `performance.repetition.${index + 1}`)]
  const script = fileURLToPath(new URL('./load.mjs', import.meta.url))
  const profileSha256 = digest(await readFile(new URL('./load-profile.json', import.meta.url)))
  const processors = cpus()
  const plan = {
    schemaVersion: 1,
    profile,
    profileSha256,
    scriptSha256: digest(await readFile(script)),
    runId: environment.runId,
    runner: { platform: process.platform, architecture: process.arch, operatingSystem: release(),
      cpuModel: processors[0]?.model ?? 'unavailable', logicalProcessors: processors.length, memoryBytes: totalmem() },
    startedAt: new Date().toISOString(),
  }
  await writeFile(join(output, 'performance-plan.json'), `${JSON.stringify(plan, null, 2)}\n`, { mode: 0o600 })
  try {
    const version = await runTool('k6', ['version'], { cwd: output, env, signal, timeoutMs: 15000 })
    const exact = version.state === 'passed' && version.output.startsWith(`k6 v${profile.k6Version} (`)
    checks.push({ id: 'performance.toolchain', required: true, state: exact ? 'passed' : version.state === 'not-run' ? 'not-run' : 'failed',
      message: `The ${profile.id} profile requires k6 ${profile.k6Version}.`, actual: version.output.trim() })
    assert.ok(exact, 'Install the exact k6 version before executing the versioned load profile.')
    signal?.throwIfAborted()
    const response = await fetch(`${environment.issuer}/protocol/openid-connect/token`, {
      method: 'POST',
      redirect: 'error',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({ grant_type: 'password', client_id: 'ngb-load', username: 'administrator', password: environment.password }),
      signal: AbortSignal.any([signal ?? new AbortController().signal, AbortSignal.timeout(15000)]),
    })
    assert.equal(response.status, 200, 'Disposable load identity could not obtain a token.')
    const token = await response.json()
    assert.ok(typeof token.access_token === 'string' && token.access_token.length > 0 && token.access_token.length <= 16384,
      'Disposable identity returned an invalid token.')
    checks.push({ id: 'performance.identity', required: true, state: 'passed', message: 'A test-only administrator token was issued by the owned identity service.' })
    for (let repetition = 1; repetition <= profile.repetitions; repetition += 1) {
      signal?.throwIfAborted()
      const id = `performance.repetition.${repetition}`
      const name = `${id}.json`
      const result = await runTool('k6', ['run', '--quiet', '--no-color', '--no-usage-report', '--address', '', script], {
        cwd: output,
        env: { ...env, NGB_LOAD_RUN_ID: environment.runId, NGB_LOAD_PROFILE_SHA256: profileSha256,
          NGB_LOAD_REPETITION: String(repetition), NGB_LOAD_OUTPUT: join(output, name),
          NGB_LOAD_ORIGIN: environment.apiOrigin, NGB_LOAD_TOKEN: token.access_token },
        signal,
        timeoutMs: 300000,
      })
      await writeFile(join(output, `${id}.log`), result.output, { mode: 0o600 })
      let evidence
      let reportSha256
      let message = `k6 ${result.reason ?? `exit ${result.exitCode}`}.`
      let state = result.state
      try {
        assert.equal(result.state, 'passed', message)
        const bytes = await readFile(join(output, name))
        assert.ok(bytes.length > 0 && bytes.length <= 1024 * 1024, 'Invalid load report size.')
        evidence = loadSummary(JSON.parse(bytes), { runId: environment.runId, repetition, profileSha256 })
        reportSha256 = digest(bytes)
        message = 'Every declared endpoint completed the exact workload and met its failure and latency budgets.'
      } catch (error) {
        state = result.state === 'not-run' ? 'not-run' : 'failed'
        message = error.message
      }
      checks.push({ id, required: true, state, message, elapsedMs: result.elapsedMs, report: name, reportSha256, evidence })
    }
  } catch (error) {
    checks.push({ id: 'performance.execution', required: true, state: 'failed', message: error.message })
  }
  for (const id of required) {
    if (!checks.some(check => check.id === id)) {
      checks.push({ id, required: true, state: 'not-run', message: 'A load prerequisite failed or execution was cancelled.' })
    }
  }
  return { checks, plan, passed: checks.every(check => check.state === 'passed') }
}
