import assert from 'node:assert/strict'
import test from 'node:test'
import { localNugetConfig, withManagedIgnores } from '../../packaging/templates/content/infrastructure/ngb/contracts.mjs'
import { blockEnd, blockStart, digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { customAdapters, installedManifest } from '../../packaging/templates/content/infrastructure/ngb/kit-plan.mjs'
import { collisionPath, customSkill, inventory, preservedPath, seedOverlay,
  verifyPreview, verifyUpgrade } from './evaluation-oracles/customized-kit-upgrade.mjs'

function input() {
  const before = {
    ...seedOverlay,
    'AGENTS.md': '# Application instructions\n\nPreserve audit history.\n',
    'ai/custom/instructions.md': 'Application policy\n',
    '.agents/skills/checkpoint-review/SKILL.md': 'Existing unrelated skill\n',
    '.gitignore': 'bin/\nobj/\n',
    'Directory.Build.props': '<Project><PropertyGroup><NgbPlatformVersion>3.3.0</NgbPlatformVersion></PropertyGroup></Project>\n',
    'Example/Example.csproj': '<Project><ItemGroup><PackageReference Include="NGB.Platform.Core" Version="$(NgbPlatformVersion)" /></ItemGroup></Project>\n',
    'Example/Behavior.cs': 'public class Behavior { }\n',
    'Example/db/V1__baseline.sql': 'CREATE SCHEMA example;\n',
    'Example/packages.lock.json': JSON.stringify({ dependencies: { net10: {
      'NGB.Platform.Core': { resolved: '3.3.0' }, 'Other.Package': { resolved: '1.0.0' },
    } } }),
    'web/package.json': JSON.stringify({ dependencies: { '@ngbplatform/ui': '3.3.0' } }, null, 2) + '\n',
    'web/package-lock.json': JSON.stringify({ packages: { 'node_modules/@ngbplatform/ui': { version: '3.3.0' } } }),
    'NuGet.Config': '<configuration><packageSources></packageSources><packageSourceMapping></packageSourceMapping></configuration>\n',
  }
  const block = `${blockStart}\nRead official instructions.\n${blockEnd}`
  const payload = {
    schemaVersion: 1,
    version: '3.4.0',
    source: { package: 'NGB.Platform.Templates', version: '3.4.0' },
    files: [
      { path: 'AGENTS.md', role: 'block', content: block },
      { path: 'CLAUDE.md', role: 'block', content: block },
      { path: collisionPath, role: 'file', content: 'Official instructions\n' },
    ].map(file => ({ ...file, sha256: digest(file.content) })),
  }
  const tooling = { 'infrastructure/ngb.mjs': '// exact supplied CLI\n' }
  const custom = customAdapters({ [customSkill]: seedOverlay[customSkill] })
  const after = {
    ...before,
    ...tooling,
    'AGENTS.md': before['AGENTS.md'] + '\n' + block + '\n',
    'CLAUDE.md': block + '\n',
    [collisionPath]: 'Official instructions\n',
    [preservedPath]: seedOverlay[collisionPath],
    '.gitignore': withManagedIgnores(before['.gitignore']),
    'Directory.Build.props': before['Directory.Build.props'].replace('3.3.0', '3.4.0'),
    'Example/packages.lock.json': before['Example/packages.lock.json'].replace('3.3.0', '3.4.0'),
    'web/package.json': before['web/package.json'].replace('3.3.0', '3.4.0'),
    'web/package-lock.json': JSON.stringify({ packages: { 'node_modules/@ngbplatform/ui': {
      version: '3.4.0', integrity: 'sha512-candidate',
    } } }),
    'ai/kit.json': JSON.stringify(installedManifest(payload, custom), null, 2) + '\n',
    'NuGet.Local.Config': localNugetConfig(before['NuGet.Config']),
    ...Object.fromEntries(Object.entries(custom).map(([path, file]) => [path, file.content])),
  }
  return { before, after, payload, tooling, version: '3.4.0', integrity: 'sha512-candidate' }
}

function preview() {
  return {
    command: 'upgrade', sourceVersion: '3.3.0', targetVersion: '3.4.0', applied: false, status: 'failed',
    plan: { planSha256: 'a'.repeat(64), entries: [
      { path: 'Directory.Build.props', state: 'update' },
      { id: 'b'.repeat(64), path: collisionPath, state: 'conflict',
        content: { local: seedOverlay[collisionPath] }, localSha256: digest(seedOverlay[collisionPath]) },
    ] },
  }
}

test('upgrade acceptance preserves application bytes and requires exact official versions and both custom adapters', () => {
  const value = input()
  const result = verifyUpgrade(value)
  assert.equal(result.checks.length, 4)
  assert.match(result.producedTreeSha256, /^[a-f0-9]{64}$/)
  assert.ok(result.changed.includes(preservedPath))
  assert.ok(!result.changed.includes('Example/Behavior.cs'))
  assert.throws(() => inventory({ '../escape': 'data' }), /path/i)
  for (const change of [
    item => { item.version = '3.5.0' },
    item => { delete item.before[customSkill] },
    item => { item.before[collisionPath] += 'changed\n' },
    item => { delete item.after[preservedPath] },
    item => { item.after[preservedPath] += 'changed\n' },
    item => { item.after['Example/Behavior.cs'] += '// changed\n' },
    item => { item.after['Example/db/V1__baseline.sql'] += '-- changed\n' },
    item => { item.after['AGENTS.md'] = item.after['AGENTS.md'].replace('Preserve audit history.', '') },
    item => { item.after['ai/custom/instructions.md'] = 'replacement' },
    item => { delete item.after['.agents/skills/checkpoint-review/SKILL.md'] },
    item => { delete item.after['.claude/skills/app-upgrade-proof/SKILL.md'] },
    item => { item.after['infrastructure/ngb.mjs'] += '// bypass\n' },
    item => { item.after[collisionPath] = item.before[collisionPath] },
    item => { item.after['ai/kit.json'] = '{}' },
    item => { item.after['web/package.json'] = item.before['web/package.json'] },
    item => { item.after['Example/packages.lock.json'] = item.before['Example/packages.lock.json'] },
    item => { item.after['Example/packages.lock.json'] = '{"dependencies":{"net10":{}}}' },
    item => { item.after['web/package-lock.json'] = item.before['web/package-lock.json'] },
    item => { item.integrity = 'sha512-another-candidate' },
    item => { item.after['unrequested.mjs'] = 'execute()' },
  ]) {
    const mutated = input()
    change(mutated)
    assert.throws(() => verifyUpgrade(mutated))
  }
})

test('upgrade preview proves read-only collision discovery and rejects incomplete or unrelated plans', () => {
  const before = inventory(input().before)
  assert.equal(verifyPreview(before, before, preview()).path, collisionPath)
  assert.throws(() => verifyPreview(before, {}, preview()), /changed the application/)
  for (const change of [
    value => { value.command = 'ai sync' },
    value => { value.sourceVersion = '3.2.0' },
    value => { value.targetVersion = '3.5.0' },
    value => { value.applied = true },
    value => { value.status = 'passed' },
    value => { value.plan.planSha256 = 'unbound' },
    value => { value.plan.entries.pop() },
    value => { value.plan.entries.push({ state: 'deleted' }) },
    value => { value.plan.entries[1].path = 'another.md' },
    value => { value.plan.entries[1].content.local = 'another source' },
    value => { value.plan.entries[1].localSha256 = 'c'.repeat(64) },
    value => { value.plan.entries[1].id = 'unbound' },
  ]) {
    const value = preview()
    change(value)
    assert.throws(() => verifyPreview(before, before, value))
  }
})
