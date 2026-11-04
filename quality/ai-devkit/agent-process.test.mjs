import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { closeSync, openSync, writeSync } from 'node:fs'
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { setTimeout as delay } from 'node:timers/promises'
import { createAgentOutputRedactor } from './agent-output.mjs'

for (const [client, family] of ['codex', 'claude'].flatMap(client =>
  ['development', 'upgrade'].map(family => [client, family]))) {
  test(`${client} ${family} journals output before completion and retains it on cancellation`, async t => {
    const output = await mkdtemp(join(tmpdir(), 'ngb-agent-process-'))
    t.after(() => rm(output, { recursive: true, force: true }))
    // Load the exact process boundary without executing the CLI's authentication,
    // dependency preparation or remote model calls. No fake agent result is accepted.
    const source = await readFile(new URL(`./evaluate-${client}-${family}.mjs`, import.meta.url), 'utf8')
    const start = source.indexOf('async function runAgent(args, cwd) {')
    const end = source.indexOf('\n}\n\ntry {', start)
    assert.ok(start > 0 && end > start, 'The bounded process implementation must be discoverable.')
    const create = new Function('spawn', 'closeSync', 'openSync', 'writeSync', 'join', 'output', 'prefix',
      'executable', 'agentEnvironment', 'abort', 'profile', 'createAgentOutputRedactor', `${source.slice(start, end + 2)}\nreturn runAgent`)
    const prepare = async (prefix, seconds = 10) => {
      await mkdir(join(output, prefix))
      const abort = new AbortController()
      const run = create(spawn, closeSync, openSync, writeSync, join, output, prefix, process.execPath,
        process.env, abort, { maximumSecondsPerAttempt: seconds }, createAgentOutputRedactor)
      return { run, abort }
    }

    const complete = await prepare('complete')
    const result = await complete.run(['--eval', 'process.stdout.write("stdout"); process.stderr.write("stderr")'], output)
    assert.equal(result.exitCode, 0)
    assert.equal(result.failure, null)
    assert.equal(await readFile(join(output, 'complete/live-transcript.jsonl'), 'utf8'), result.stdout)
    assert.equal(await readFile(join(output, 'complete/live-stderr.log'), 'utf8'), result.stderr)

    const cancelled = await prepare('cancelled')
    const running = cancelled.run(['--eval', 'process.stdout.write("partial output\\nhttp://synthetic-user:partial-password"); setInterval(() => {}, 1000)'], output)
    try {
      const deadline = Date.now() + 5000
      let partial = ''
      while (!partial && Date.now() < deadline) {
        partial = await readFile(join(output, 'cancelled/live-transcript.jsonl'), 'utf8')
        if (!partial) await delay(10)
      }
      assert.equal(partial, 'partial output\n', 'Output must be durable while the client is still running.')
    } finally {
      cancelled.abort.abort()
    }
    const stopped = await running
    assert.equal(stopped.failure, 'cancelled')
    assert.equal(stopped.stdout, 'partial output\n[REDACTED INCOMPLETE URL]')
    assert.equal(await readFile(join(output, 'cancelled/live-transcript.jsonl'), 'utf8'), stopped.stdout)

    const redacted = await prepare('redacted')
    const secretEvent = JSON.stringify({ type: 'tool_result', text: 'http://synthetic-user:synthetic-pass@127.0.0.1:1234' })
    const code = `process.stdout.write(${JSON.stringify(secretEvent.slice(0, 40))});
      setTimeout(() => {
        process.stdout.write(${JSON.stringify(secretEvent.slice(40) + '\n')});
        process.stderr.write('Bearer synthetic-token');
      }, 20)`
    const safe = await redacted.run(['--eval', code], output)
    assert.equal(safe.exitCode, 0)
    assert.deepEqual(JSON.parse(safe.stdout), { type: 'tool_result', text: 'http://[REDACTED]@127.0.0.1:1234' })
    assert.equal(safe.stderr, 'Bearer [REDACTED]')
    assert.equal(await readFile(join(output, 'redacted/live-transcript.jsonl'), 'utf8'), safe.stdout)
    assert.equal(await readFile(join(output, 'redacted/live-stderr.log'), 'utf8'), safe.stderr)

    const timed = await prepare('timeout', 0.1)
    assert.equal((await timed.run(['--eval', 'setInterval(() => {}, 1000)'], output)).failure, 'timeout')
    const collision = await prepare('collision')
    await writeFile(join(output, 'collision/live-transcript.jsonl'), 'existing evidence')
    await assert.rejects(collision.run(['--eval', 'process.exit(0)'], output), { code: 'EEXIST' })
    assert.equal(await readFile(join(output, 'collision/live-transcript.jsonl'), 'utf8'), 'existing evidence')
  })
}
