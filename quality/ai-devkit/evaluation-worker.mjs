import assert from 'node:assert/strict'
import { pathToFileURL } from 'node:url'
import { resolve } from 'node:path'
import { lockRun } from '../external-consumer/checkpoints.mjs'

const [script, ...args] = process.argv.slice(2)
assert.match(script, /^quality\/ai-devkit\/(?:prepare-(?:development-fixture|upgrade-evaluation)|evaluate-(?:codex|claude)-(?:discovery|development|upgrade)|run-(?:development|upgrade)-oracle|review-agent-source)\.mjs$/)
const parent = process.ppid
const unlock = await lockRun(process.env.NGB_AI_WORKER_LEASE)
const watchdog = setInterval(() => {
  if (process.ppid !== parent) {
    clearInterval(watchdog)
    process.kill(process.pid, 'SIGINT')
  }
}, 500)
watchdog.unref()
try {
  process.argv = [process.execPath, resolve(script), ...args]
  await import(pathToFileURL(resolve(script)).href)
} finally {
  clearInterval(watchdog)
  await unlock()
}
