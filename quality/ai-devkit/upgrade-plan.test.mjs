import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import { blockEnd, blockStart, digest, resolveConflicts } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { materializeInitialKitUpgrade, planInitialKitUpgrade, toolingBaseline } from '../../packaging/templates/content/infrastructure/ngb/upgrade-plan.mjs'

const baseline = JSON.parse(await readFile(new URL('../../ai-devkit/baselines/cli-3.3.0.json', import.meta.url), 'utf8'))

function input() {
  const files = [
    { path: 'AGENTS.md', role: 'block', content: `${blockStart}\nRead the Kit.\n${blockEnd}` },
    { path: 'ai/vendor/instructions.md', role: 'file', content: 'Official instructions\n' },
  ].map(file => ({ ...file, sha256: digest(file.content) }))
  const incoming = { schemaVersion: 1, version: '3.4.0', source: { package: 'NGB.Platform.Templates', version: '3.4.0' }, files }
  const tooling = Object.fromEntries(baseline.files.map(file => [file.path, file.content.replaceAll('3.3.0', '3.4.0')]))
  tooling['infrastructure/validation/Ngb.Validation.csproj'] = '<Project />'
  const current = { 'ai/kit.json': null, 'AGENTS.md': '# User instructions\r\n', 'ai/vendor/instructions.md': null,
    ...Object.fromEntries(baseline.files.map(file => [file.path, file.content])),
    'infrastructure/validation/Ngb.Validation.csproj': null }
  return { baseline, incoming, tooling, current, artifactIdentity: 'a'.repeat(64) }
}

test('initial Kit installation binds the published source, target artifact and exact local snapshot', () => {
  const value = input()
  assert.equal(toolingBaseline(baseline), baseline)
  const plan = planInitialKitUpgrade(value)
  const changes = materializeInitialKitUpgrade(plan, value.current)
  assert.equal(changes['AGENTS.md'], '# User instructions\r\n\n' + value.incoming.files[0].content + '\n')
  assert.equal(JSON.parse(changes['ai/kit.json']).version, '3.4.0')
  assert.equal(changes['infrastructure/ngb/contracts.mjs'], value.tooling['infrastructure/ngb/contracts.mjs'])
  assert.equal(plan.source, baseline.source.sha256)
  const changed = structuredClone(value)
  changed.current['AGENTS.md'] += 'Concurrent edit\n'
  assert.notEqual(planInitialKitUpgrade(changed).sha256, plan.sha256)
  changed.artifactIdentity = 'b'.repeat(64)
  assert.notEqual(planInitialKitUpgrade(changed).target, plan.target)
})

test('modified CLI, helper collisions and an existing user Kit manifest require reviewed decisions', () => {
  const value = input()
  value.current['infrastructure/ngb/cli.mjs'] += '\n// local change\n'
  value.current['infrastructure/validation/Ngb.Validation.csproj'] = '<Project>user helper</Project>'
  value.current['ai/kit.json'] = '{"user":"manifest"}\n'
  value.current['ai/vendor/instructions.md'] = 'User file at a future official path\n'
  const plan = planInitialKitUpgrade(value)
  assert.equal(plan.entries.filter(entry => entry.state === 'conflict').length, 4)
  assert.throws(() => materializeInitialKitUpgrade(plan, value.current), /required target tool contract/)
  const decisions = plan.entries.filter(entry => entry.state === 'conflict').map(entry => ({ id: entry.id, choice: 'incoming' }))
  const resolved = resolveConflicts(plan, { schemaVersion: 1, planSha256: plan.sha256, decisions })
  const changes = materializeInitialKitUpgrade(resolved, value.current)
  assert.equal(changes['ai/vendor/instructions.md'], value.incoming.files[1].content)
  assert.equal(changes['infrastructure/ngb/cli.mjs'], value.tooling['infrastructure/ngb/cli.mjs'])
  const local = resolveConflicts(plan, { schemaVersion: 1, planSha256: plan.sha256,
    decisions: decisions.map(decision => ({ ...decision, choice: 'local' })) })
  assert.throws(() => materializeInitialKitUpgrade(local, value.current), /required target tool contract/)
})

test('custom canonical skills render for both agents without claiming unrelated skills', () => {
  const value = input()
  value.customSources = { 'ai/custom/skills/app-review/SKILL.md': '---\nname: app-review\ndescription: Review changes.\n---\n' }
  value.current['.agents/skills/app-review/SKILL.md'] = null
  value.current['.claude/skills/app-review/SKILL.md'] = null
  const changes = materializeInitialKitUpgrade(planInitialKitUpgrade(value), value.current)
  assert.match(changes['.agents/skills/app-review/SKILL.md'], /name: app-review\ndescription: Review changes\./)
  assert.match(changes['.agents/skills/app-review/SKILL.md'], /\.\.\/\.\.\/\.\.\/ai\/custom\/skills\/app-review\/SKILL\.md/)
  assert.equal(changes['.claude/skills/app-review/SKILL.md'], changes['.agents/skills/app-review/SKILL.md'])
  assert.ok(!Object.hasOwn(changes, 'ai/custom/skills/app-review/SKILL.md'))
})

test('source integrity, tooling ownership and transition identity fail closed', () => {
  for (const change of [
    value => { value.baseline.schemaVersion = 2 },
    value => { value.baseline.version = '3.2.0' },
    value => { value.baseline.files.pop() },
    value => { value.baseline.files[0].content = null },
    value => { value.baseline.files[0].content += 'modified' },
    value => {
      value.incoming.version = '3.5.0'
      value.incoming.source.version = '3.5.0'
    },
    value => { value.artifactIdentity = 'unverified' },
    value => { value.tooling['app.cs'] = 'outside ownership' },
    value => { value.tooling['infrastructure/ngb/cli.mjs'] = null },
    value => { delete value.current['infrastructure/ngb/cli.mjs'] },
    value => { delete value.tooling['infrastructure/ngb/cli.mjs'] },
    value => { delete value.current['ai/kit.json'] },
  ]) {
    const value = structuredClone(input())
    change(value)
    assert.throws(() => planInitialKitUpgrade(value))
  }
})
