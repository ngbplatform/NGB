import assert from 'node:assert/strict'
import { planUpgrade, localNugetConfig, withManagedIgnores } from '../../../packaging/templates/content/infrastructure/ngb/contracts.mjs'
import { digest, portablePath, replaceBlock } from '../../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { customAdapters, installedManifest, validateInstalled } from '../../../packaging/templates/content/infrastructure/ngb/kit-plan.mjs'

export const collisionPath = 'ai/vendor/instructions.md'
export const preservedPath = 'ai/custom/legacy-instructions.md'
export const customSkill = 'ai/custom/skills/app-upgrade-proof/SKILL.md'
export const seedOverlay = {
  [collisionPath]: '# Existing application policy\n\nPreserve checkpoint audit evidence and review application changes before deployment.\n',
  [customSkill]: '---\nname: app-upgrade-proof\ndescription: Review application upgrade decisions and retained audit evidence.\n---\n\nRead ../../instructions.md and preserve the application checkpoint history.\n',
}

export function inventory(files) {
  const rows = Object.entries(files).map(([path, bytes]) => {
    portablePath(path)
    return [path, digest(bytes)]
  })
  return Object.fromEntries(rows.sort(([left], [right]) => left.localeCompare(right, 'en')))
}

export function verifyPreview(before, after, preview) {
  assert.deepEqual(after, before, 'Upgrade preview changed the application.')
  assert.equal(preview.command, 'upgrade')
  assert.equal(preview.sourceVersion, '3.3.0')
  assert.equal(preview.targetVersion, '3.4.0')
  assert.equal(preview.applied, false)
  assert.equal(preview.status, 'failed', 'The declared ownership collision must be reported.')
  assert.match(preview.plan.planSha256, /^[a-f0-9]{64}$/)
  const conflicts = preview.plan.entries.filter(entry => ['conflict', 'deleted'].includes(entry.state))
  assert.equal(conflicts.length, 1, 'The fixture must have exactly one ownership collision.')
  assert.equal(conflicts[0].path, collisionPath)
  assert.equal(conflicts[0].content.local, seedOverlay[collisionPath])
  assert.equal(conflicts[0].localSha256, digest(seedOverlay[collisionPath]))
  assert.match(conflicts[0].id, /^[a-f0-9]{64}$/)
  return conflicts[0]
}

export function verifyUpgrade({ before, after, payload, tooling, version, integrity }) {
  assert.equal(version, '3.4.0')
  const beforeInventory = inventory(before)
  const afterInventory = inventory(after)
  for (const [path, content] of Object.entries(seedOverlay)) {
    assert.equal(before[path]?.toString('utf8'), content, `The trusted upgrade seed changed: ${path}`)
  }
  const texts = Object.fromEntries(Object.entries(before).map(([path, bytes]) => [path, bytes.toString('utf8')]))
  const dependencies = planUpgrade(texts, version)
  assert.equal(dependencies.source, '3.3.0')
  const custom = customAdapters(Object.fromEntries(Object.entries(texts)
    .filter(([path]) => path.startsWith('ai/custom/skills/'))))
  const expected = { ...texts, ...dependencies.changes, ...tooling }
  for (const file of payload.files) {
    expected[file.path] = file.role === 'block' ? replaceBlock(texts[file.path] ?? '', file.content) : file.content
  }
  for (const [path, adapter] of Object.entries(custom)) expected[path] = adapter.content
  expected[preservedPath] = texts[collisionPath]
  expected['ai/kit.json'] = JSON.stringify(installedManifest(payload, custom), null, 2) + '\n'
  expected['.gitignore'] = withManagedIgnores(texts['.gitignore'])
  expected['NuGet.Local.Config'] = localNugetConfig(texts['NuGet.Config'])
  assert.deepEqual(Object.keys(after).sort(), Object.keys(expected).sort(),
    'The upgrade added or removed files outside the declared migration contract.')
  for (const [path, content] of Object.entries(expected)) {
    if (path.endsWith('/packages.lock.json') || path === 'web/package-lock.json') continue
    assert.equal(after[path].toString('utf8'), content, `Upgrade content or preservation mismatch: ${path}`)
  }
  validateInstalled(JSON.parse(after['ai/kit.json']), payload)
  for (const path of Object.keys(before).filter(path => path.endsWith('/packages.lock.json'))) {
    const lock = JSON.parse(after[path])
    let found = false
    for (const framework of Object.values(lock.dependencies)) {
      for (const [name, dependency] of Object.entries(framework)) {
        if (!name.startsWith('NGB.Platform.')) continue
        found = true
        assert.equal(dependency.resolved, version, `Stale SDK lock: ${path}/${name}`)
      }
    }
    assert.ok(found, `No SDK dependency remains in ${path}.`)
  }
  const ui = JSON.parse(after['web/package-lock.json']).packages['node_modules/@ngbplatform/ui']
  assert.equal(ui.version, version, 'The UI lock still targets an older platform.')
  assert.equal(ui.integrity, integrity, 'The UI lock does not identify the supplied candidate.')
  return {
    producedTreeSha256: digest(JSON.stringify(Object.entries(afterInventory).sort())),
    changed: Object.keys(afterInventory).filter(path => beforeInventory[path] !== afterInventory[path]).sort(),
    checks: ['custom-content-preserved', 'coordinated-upgrade', 'both-agent-adapters', 'exact-official-content'],
  }
}
