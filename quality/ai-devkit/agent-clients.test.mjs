import assert from 'node:assert/strict'
import { mkdtemp, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { discoverAgentClients } from './agent-clients.mjs'

test('agent preflight requires pinned, authenticated clients and strips API credentials', async t => {
  const root = await mkdtemp(join(tmpdir(), 'ngb-agent-clients-'))
  t.after(() => rm(root, { recursive: true, force: true }))
  const matrix = { platform: process.platform, architecture: process.arch, clients: ['codex', 'claude'] }
  const profiles = Object.fromEntries(matrix.clients.map(client => [client, {
    discovery: { clientVersion: '1.2.3' }, development: { clientVersion: '1.2.3' }, upgrade: { clientVersion: '1.2.3' },
  }]))
  const source = { HOME: root, PATH: root, OPENAI_API_KEY: 'must-not-propagate', ANTHROPIC_API_KEY: 'must-not-propagate' }
  const install = async (client, { version = '1.2.3', loggedIn = true } = {}) => {
    const path = join(root, client)
    await writeFile(path, `#!${process.execPath}
const assert = require('node:assert/strict')
assert.equal(process.env.OPENAI_API_KEY, undefined)
assert.equal(process.env.ANTHROPIC_API_KEY, undefined)
assert.equal(process.env.DISABLE_AUTOUPDATER, '1')
if (process.argv[2] === '--version') console.log(${JSON.stringify(client === 'codex' ? `codex-cli ${version}` : `${version} (Claude Code)`)})
else console.log(${JSON.stringify(client === 'codex' ? loggedIn ? 'Logged in using ChatGPT' : 'Not logged in'
      : JSON.stringify({ loggedIn, authMethod: 'claude.ai' }))})
`, { mode: 0o700 })
    source[`NGB_AI_${client.toUpperCase()}_BINARY`] = path
  }
  await install('codex')
  await install('claude')
  const clients = await discoverAgentClients(root, matrix, profiles, source)
  assert.deepEqual(Object.keys(clients), matrix.clients)
  assert.match(clients.codex.sha256, /^[a-f0-9]{64}$/)
  await install('codex', { version: '9.9.9' })
  await assert.rejects(discoverAgentClients(root, matrix, profiles, source), /Install the frozen codex/)
  await install('codex', { loggedIn: false })
  await assert.rejects(discoverAgentClients(root, matrix, profiles, source), /Logged in using ChatGPT/)
  await install('codex')
  await install('claude', { loggedIn: false })
  await assert.rejects(discoverAgentClients(root, matrix, profiles, source), /Sign in to Claude/)
  await assert.rejects(discoverAgentClients(root, { ...matrix, platform: 'unsupported' }, profiles, source), /terminal environment/)
  source.NGB_AI_CODEX_BINARY = join(root, 'missing')
  await assert.rejects(discoverAgentClients(root, matrix, profiles, source), { code: 'ENOENT' })
})
