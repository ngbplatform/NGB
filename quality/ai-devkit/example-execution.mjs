import assert from 'node:assert/strict'
import { mkdir, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { runTool } from '../../packaging/templates/content/infrastructure/ngb/process.mjs'

// Match the existing authored-integration budget. The enclosing application
// check retains its one-hour limit, including restore, build and frontend work.
export const exampleTestTimeoutMs = 45 * 60_000

export function exampleRunner({ output, env, signal, candidateSha256 }) {
  return async (id, args, cwd, tool = 'dotnet', timeoutMs = 600_000) => {
    signal.throwIfAborted()
    const result = await runTool(tool, args, {
      cwd, env, timeoutMs, signal, maxBytes: 8 * 1024 * 1024,
      onSpawn: async pid => {
        await mkdir(join(output, 'processes'), { recursive: true })
        await writeFile(join(output, 'processes', `${id}.json`), `${JSON.stringify({
          schemaVersion: 1, candidateSha256, runnerPid: process.pid, childPid: pid,
          startedAt: new Date().toISOString(), tool, args, cwd, timeoutMs,
          purpose: 'Diagnostic identity only; this receipt does not establish current process ownership.',
        }, null, 2)}\n`, { mode: 0o600, flag: 'wx' })
        console.log(`Independent example step: ${id} (limit ${timeoutMs / 60_000} min).`)
      },
    })
    const log = join(output, `${id}.log`)
    await writeFile(log, result.output, { mode: 0o600, flag: 'wx' })
    await writeFile(join(output, `${id}.result.json`), `${JSON.stringify({
      schemaVersion: 1, candidateSha256, id, tool, state: result.state,
      exitCode: result.exitCode, reason: result.reason, elapsedMs: result.elapsedMs, timeoutMs,
    }, null, 2)}\n`, { mode: 0o600, flag: 'wx' })
    const summary = `[${result.state}] ${id}: ${(result.elapsedMs / 1000).toFixed(1)}s`
    console.log(summary)
    assert.equal(result.state, 'passed',
      `${summary}; reason: ${result.reason ?? result.exitCode}; log: ${log}`)
    return result.output
  }
}
