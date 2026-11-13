import assert from 'node:assert/strict'
import { execFile } from 'node:child_process'
import { readFile, readdir, realpath } from 'node:fs/promises'
import { homedir } from 'node:os'
import { delimiter, join } from 'node:path'
import { promisify } from 'node:util'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'

const exec = promisify(execFile)

export async function discoverAgentClients(repository, matrix, profiles, source = process.env) {
  assert.equal(process.platform, matrix.platform, 'Live agent evaluations require the declared macOS terminal environment.')
  assert.equal(process.arch, matrix.architecture)
  const env = Object.fromEntries(['HOME', 'USER', 'LOGNAME', 'PATH', 'TMPDIR', 'LANG', 'LC_ALL', 'TERM']
    .filter(name => source[name] !== undefined).map(name => [name, source[name]]))
  env.DISABLE_AUTOUPDATER = '1'
  const clients = {}
  for (const client of matrix.clients) {
    const version = profiles[client].discovery.clientVersion
    for (const profile of Object.values(profiles[client])) assert.equal(profile.clientVersion, version)
    const override = source[`NGB_AI_${client.toUpperCase()}_BINARY`]
    const candidates = override ? [override] : [
      ...source.PATH?.split(delimiter).map(path => join(path, client)) ?? [],
      join(homedir(), '.local/bin', client),
    ]
    if (!override && client === 'codex') {
      candidates.unshift(join(repository, `artifacts/certification-tools/codex-${version}-darwin-arm64`,
        'node_modules/@openai/codex-darwin-arm64/vendor/aarch64-apple-darwin/bin/codex'))
    }
    if (!override && client === 'claude') {
      const root = join(homedir(), 'Library/Application Support/Claude/claude-code', version)
      const entries = await readdir(root, { withFileTypes: true }).catch(error => {
        if (error.code !== 'ENOENT') throw error
        return []
      })
      for (const entry of entries.filter(entry => entry.isDirectory())) {
        candidates.push(join(root, entry.name, 'claude.app/Contents/MacOS/claude'))
      }
    }
    for (const path of candidates) {
      try {
        const executable = await realpath(path)
        const result = await exec(executable, ['--version'], { env, timeout: 15000, maxBuffer: 1024 * 1024 })
        const expected = client === 'codex' ? `codex-cli ${version}` : `${version} (Claude Code)`
        if (result.stdout.trim() !== expected) continue
        clients[client] = { executable, version, sha256: digest(await readFile(executable)) }
        break
      } catch (error) {
        if (override) throw error
      }
    }
    assert.ok(clients[client], `Install the frozen ${client} ${version}, or set NGB_AI_${client.toUpperCase()}_BINARY to that executable.`)
    const args = client === 'codex' ? ['login', 'status'] : ['auth', 'status']
    let auth
    try {
      auth = await exec(clients[client].executable, args, { env, timeout: 15000, maxBuffer: 1024 * 1024 })
    } catch {
      throw new Error(`${client} subscription login is unavailable. Run ${client === 'codex' ? 'codex login' : 'claude auth login'} in your terminal, then resume certification.`)
    }
    if (client === 'codex') assert.match(auth.stdout + auth.stderr, /Logged in using ChatGPT/)
    else {
      const status = JSON.parse(auth.stdout)
      assert.ok(status.loggedIn && status.authMethod === 'claude.ai', 'Sign in to Claude Code with the existing subscription.')
    }
  }
  return clients
}
