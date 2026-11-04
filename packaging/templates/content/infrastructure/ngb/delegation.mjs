import assert from 'node:assert/strict'
import { mkdtemp, realpath, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { acquireCli } from './acquisition.mjs'
import { loadCandidate } from './application.mjs'
import { exactVersion, toolVersion } from './contracts.mjs'
import { executionEnvironment, runTool } from './process.mjs'

const markerName = 'NGB_CLI_DELEGATION'

export function needsDelegation(options, version = toolVersion) {
  if (options.command !== 'upgrade') return false
  exactVersion(options.to)
  const [major, minor] = options.to.split('.').map(Number)
  return options.to !== version && (major > 3 || major === 3 && minor >= 4)
}

export async function validateDelegation(options, environment = process.env, entry = process.argv[1], version = toolVersion) {
  const marker = environment[markerName]
  if (marker === undefined) return
  assert.ok(marker.length <= 16384, 'Invalid CLI delegation marker.')
  const value = JSON.parse(marker)
  assert.equal(value.schemaVersion, 1, 'Unsupported CLI delegation schema.')
  assert.equal(options.command, 'upgrade', 'Only a coordinated upgrade can delegate to another CLI.')
  assert.equal(value.version, version, 'The acquired CLI implementation does not match its requested version.')
  assert.equal(options.to, version, 'Recursive CLI delegation is not allowed.')
  assert.match(value.integrity, /^sha512-[A-Za-z0-9+/]{86}==$/, 'Missing acquired CLI integrity.')
  assert.equal(await realpath(entry), value.entry, 'A different CLI entry point was invoked after acquisition.')
}

export async function delegateUpgrade(args, options, settings, { signal, cacheRoot, acquire = acquireCli,
  execute = runTool, environment = process.env } = {}) {
  assert.equal(environment[markerName], undefined, 'Recursive CLI delegation is not allowed.')
  const root = await realpath(resolve(options.app ?? process.cwd()))
  const candidate = options.local || options.packages ? await loadCandidate(settings.packagesDirectory) : null
  const acquired = await acquire(options.to, { candidate, cacheRoot, applicationRoot: root, signal })
  assert.equal(acquired.version, options.to, 'Acquired CLI version differs from the requested target.')
  const entry = await realpath(acquired.entry)
  const temporary = await mkdtemp(join(tmpdir(), 'ngb-cli-delegation-'))
  try {
    const env = executionEnvironment(temporary, {
      [markerName]: JSON.stringify({ schemaVersion: 1, version: acquired.version, integrity: acquired.integrity, entry }),
    }, environment)
    // Keep the original working directory: relative --app, --packages and
    // --resolutions arguments must retain their meaning in the target CLI.
    const forwarded = [...args]
    if (options.local && !options.packages) forwarded.push('--packages', settings.packagesDirectory)
    const result = await execute('node', [entry, ...forwarded], { cwd: process.cwd(), env, signal,
      timeoutMs: 3600000, maxBytes: 16 * 1024 * 1024 })
    return { ...result, acquisition: { version: acquired.version, integrity: acquired.integrity, source: acquired.source } }
  } finally {
    await rm(temporary, { recursive: true, force: true })
  }
}
