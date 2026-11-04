import assert from 'node:assert/strict'
import test from 'node:test'
import {
  blockEnd, blockStart, digest, distinctPaths, managedBlock, objectKeys, officialPath,
  portablePath, reconcile, replaceBlock, resolveConflicts, textBytes, validatePayload,
} from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import {
  customAdapters, installedManifest, materializePlan, payloadIdentity, planKit, planSummary, validateInstalled,
} from '../../packaging/templates/content/infrastructure/ngb/kit-plan.mjs'

const instruction = content => `${blockStart}\n${content}\n${blockEnd}`

function payload(version = '3.4.0', content = 'Official guidance\n') {
  return {
    schemaVersion: 1,
    version,
    source: { package: 'NGB.Platform.Templates', version },
    files: [
      { path: 'ai/vendor/instructions.md', role: 'file', content, sha256: digest(content) },
      { path: 'AGENTS.md', role: 'block', content: instruction('Read ai/vendor/instructions.md.'),
        sha256: digest(instruction('Read ai/vendor/instructions.md.')) },
    ],
  }
}

function installedFiles(value) {
  return Object.fromEntries([
    ...value.files.map(file => [file.path, file.content]),
    ['ai/kit.json', `${JSON.stringify(installedManifest(value), null, 2)}\n`],
  ])
}

test('portable paths reject traversal, aliases, Windows devices and case collisions', () => {
  assert.equal(portablePath('ai/vendor/references/API.md'), 'ai/vendor/references/API.md')
  for (const path of ['', '../escape', '/absolute', 'a//b', 'a/./b', 'C:/secret', 'a\\b',
    'a\0b', 'a\nb', 'a.', 'a ', 'CON', 'Nul.txt', 'com1.json', 'lpt9', 'a?b', 'a*b',
    'a|b', 'a<b', 'a>b', 'a"b', 'e\u0301.md', 'a'.repeat(241)]) {
    assert.throws(() => portablePath(path), undefined, path)
  }
  assert.throws(() => distinctPaths(['ai/Readme.md', 'ai/README.md']), /collision/)
  assert.throws(() => distinctPaths(['ai/vendor', 'ai/vendor/file.md']), /also a directory/)
  assert.throws(() => distinctPaths(Array(4097).fill('a')), /size limit/)
  distinctPaths(['ai/a.md', 'ai/b.md'])
})

test('data-only schemas and payload hashes cannot expand ownership', () => {
  const valid = payload()
  assert.equal(validatePayload(valid), valid)
  assert.equal(textBytes('hello\n').toString(), 'hello\n')
  for (const value of [null, [], 'a']) assert.throws(() => objectKeys(value, []))
  assert.throws(() => objectKeys({}, ['required']), /Missing/)
  assert.throws(() => objectKeys({ hooks: [] }, []), /Unknown/)
  for (const [path, role] of [['ai/custom/instructions.md', 'file'], ['.env', 'file'],
    ['Program.cs', 'file'], ['AGENTS.md', 'file'], ['ai/vendor/a.md', 'block']]) {
    assert.throws(() => officialPath(path, role))
  }
  officialPath('.agents/skills/ngb-test/SKILL.md', 'file')
  officialPath('.claude/skills/ngb-test/SKILL.md', 'file')
  assert.equal(textBytes('a'.repeat(2 * 1024 * 1024)).length, 2 * 1024 * 1024)
  assert.throws(() => textBytes('é'.repeat(1024 * 1024 + 1)), /size limit/)
  for (const value of [null, 'a\r\nb', '\uFEFFa', 'a\0b', '\uD800', 'a'.repeat(2 * 1024 * 1024 + 1)]) {
    assert.throws(() => textBytes(value))
  }
  for (const change of [
    value => { value.schemaVersion = 2 },
    value => { value.version = 'latest' },
    value => { value.source.package = 'untrusted' },
    value => { value.source.version = '3.5.0' },
    value => { value.files = [] },
    value => { value.files.push(value.files[0]) },
    value => { value.files[0].content = 'tampered' },
    value => { value.files[0].path = 'ai/custom/instructions.md' },
    value => { value.files[0].hook = 'execute' },
    value => {
      value.files[1].content = `prefix${value.files[1].content}`
      value.files[1].sha256 = digest(value.files[1].content)
    },
  ]) {
    const invalid = structuredClone(valid)
    change(invalid)
    assert.throws(() => validatePayload(invalid))
  }
  const oversized = payload()
  oversized.files = Array.from({ length: 17 }, (_, index) => {
    const content = 'a'.repeat(1048576)
    return { path: `ai/vendor/${index}.md`, role: 'file', content, sha256: digest(content) }
  })
  assert.throws(() => validatePayload(oversized), /total size/)
})

test('managed root edits preserve application text byte-for-byte and reject ambiguous markers', () => {
  const original = `User policy\r\n\r\n${instruction('old')}\r\nTail\r\n`
  assert.equal(replaceBlock(original, instruction('new')),
    `User policy\r\n\r\n${instruction('new')}\r\nTail\r\n`)
  assert.equal(replaceBlock(original, null), 'User policy\r\n\r\n\r\nTail\r\n')
  assert.equal(managedBlock('user text'), null)
  assert.equal(replaceBlock('', instruction('new')), `${instruction('new')}\n`)
  assert.equal(replaceBlock('user\n', instruction('new')), `user\n\n${instruction('new')}\n`)
  assert.equal(replaceBlock('user', instruction('new')), `user\n\n${instruction('new')}\n`)
  assert.equal(replaceBlock('user', null), 'user')
  for (const text of [blockStart, blockEnd, blockEnd + blockStart, instruction('a') + instruction('b')]) {
    assert.throws(() => managedBlock(text), /markers/)
  }
})

test('three-way reconciliation never guesses semantic text merges or adopts unowned files', () => {
  for (const [base, local, incoming, expected] of [
    [null, null, 'new', 'update'], [null, 'new', 'new', 'conflict'],
    ['old', 'old', 'new', 'update'], ['old', 'old', 'old', 'unchanged'],
    ['old', 'custom', 'old', 'customized'], ['old', 'custom', 'new', 'conflict'],
    ['old', null, 'old', 'deleted'], ['old', null, null, 'deleted'],
    ['old', 'old', null, 'update'], ['old', 'custom', null, 'conflict'],
    ['old', 'new', 'new', 'converged'],
  ]) assert.equal(reconcile(base, local, incoming), expected)
})

test('first install inserts bounded instructions without claiming application files', () => {
  const incoming = payload()
  const current = { 'AGENTS.md': 'User rules\r\n', 'ai/vendor/instructions.md': null, 'ai/kit.json': null }
  const plan = planKit({ baseline: null, incoming, installed: null, current })
  const changes = materializePlan(plan, current)
  assert.equal(changes['AGENTS.md'], `User rules\r\n\n${incoming.files[1].content}\n`)
  assert.equal(changes['ai/vendor/instructions.md'], incoming.files[0].content)
  assert.equal(JSON.parse(changes['ai/kit.json']).versions.sdk, '3.4.0')
  assert.equal(plan.source, null)
  assert.equal(plan.target, payloadIdentity(incoming))
  assert.throws(() => planKit({ baseline: incoming, incoming, installed: null, current }))
  assert.throws(() => planKit({ baseline: null, incoming, installed: null, current: {} }), /snapshot/)
})

test('updates preserve customization, require hash-bound conflict choices and detect stale decisions', () => {
  const baseline = payload()
  const incoming = payload('3.5.0', 'New official guidance\n')
  const current = installedFiles(baseline)
  current['ai/vendor/instructions.md'] = 'Local guidance\n'
  const plan = planKit({ baseline, incoming, installed: installedManifest(baseline), current })
  const conflict = plan.entries.find(entry => entry.state === 'conflict')
  assert.ok(conflict)
  assert.throws(() => materializePlan(plan, current), /unresolved/)
  const resolution = choice => ({ schemaVersion: 1, planSha256: plan.sha256,
    decisions: [{ id: conflict.id, choice }] })
  const resolved = resolveConflicts(plan, resolution('incoming'))
  assert.equal(materializePlan(resolved, current)['ai/vendor/instructions.md'], incoming.files[0].content)
  assert.equal(materializePlan(resolveConflicts(plan, resolution('local')), current)['ai/vendor/instructions.md'], undefined)
  const replacement = resolution('replacement')
  replacement.decisions[0].content = 'Reviewed text\n'
  assert.equal(materializePlan(resolveConflicts(plan, replacement), current)['ai/vendor/instructions.md'], 'Reviewed text\n')
  for (const change of [
    value => { value.schemaVersion = 2 },
    value => { value.planSha256 = 'stale' },
    value => { value.decisions = null },
    value => { value.decisions.push(value.decisions[0]) },
    value => { value.decisions[0].choice = 'force' },
    value => { value.decisions[0].content = 'unexpected' },
    value => { value.decisions[0].id = 'unknown' },
    value => { value.decisions[0].id = plan.entries.find(entry => entry.state === 'unchanged').id },
  ]) {
    const invalid = resolution('incoming')
    change(invalid)
    assert.throws(() => resolveConflicts(plan, invalid))
  }
  const changed = planKit({ baseline, incoming, installed: installedManifest(baseline),
    current: { ...current, 'AGENTS.md': `${current['AGENTS.md']}\nNew user policy` } })
  assert.throws(() => resolveConflicts(changed, resolution('incoming')), /stale/)
  assert.deepEqual(resolveConflicts(plan, { schemaVersion: 1, planSha256: plan.sha256, decisions: [] }).entries, plan.entries)
  assert.equal(planSummary(plan).entries.find(entry => entry.id === conflict.id).localSha256, digest(conflict.local))
  assert.equal(planSummary(plan).entries.find(entry => entry.id === conflict.id).content.local, conflict.local)
})

test('upstream removal and local deletion remain distinct from an unchanged install', () => {
  const baseline = payload()
  const current = installedFiles(baseline)
  const incoming = payload()
  incoming.files = incoming.files.filter(file => file.path !== 'ai/vendor/instructions.md')
  const removed = planKit({ baseline, incoming, installed: installedManifest(baseline), current })
  assert.equal(materializePlan(removed, current)['ai/vendor/instructions.md'], null)
  current['AGENTS.md'] = null
  const deleted = planKit({ baseline, incoming, installed: installedManifest(baseline), current })
  assert.equal(deleted.entries.find(entry => entry.path === 'AGENTS.md').state, 'deleted')
  const entry = deleted.entries.find(item => item.path === 'AGENTS.md')
  const badBlock = resolveConflicts(deleted, { schemaVersion: 1, planSha256: deleted.sha256,
    decisions: [{ id: entry.id, choice: 'replacement', content: 'no markers' }] })
  assert.throws(() => materializePlan(badBlock, current), /managed block/)
  const unchanged = planKit({ baseline, incoming: baseline, installed: installedManifest(baseline), current: installedFiles(baseline) })
  assert.deepEqual(materializePlan(unchanged, installedFiles(baseline)), {})
})

test('custom skills have one canonical source and bounded adapter ownership', () => {
  const sources = { 'ai/custom/skills/app-check/SKILL.md': '---\nname: app-check\ndescription: Check app rules.\n---\nRead rules.md\n',
    'ai/custom/skills/app-check/rules.md': 'Application rules\n' }
  const adapters = customAdapters(sources)
  assert.equal(Object.keys(adapters).length, 2)
  assert.equal(adapters['.agents/skills/app-check/SKILL.md'].content, adapters['.claude/skills/app-check/SKILL.md'].content)
  assert.match(adapters['.agents/skills/app-check/SKILL.md'].content, /\.\.\/\.\.\/\.\.\/ai\/custom\/skills\/app-check\/SKILL\.md/)
  assert.ok(!Object.hasOwn(adapters, '.agents/skills/app-check/rules.md'))
  const baseline = payload()
  const manifest = installedManifest(baseline, adapters)
  assert.equal(validateInstalled(manifest, baseline), manifest)
  for (const invalid of [
    { 'ai/custom/skills/ngb-check/SKILL.md': 'bad' },
    { 'ai/custom/skills/app-check/rules.md': 'missing skill' },
    { 'ai/custom/skills/app-check/SKILL.md': '---\nname: app-other\ndescription: no\n---\n' },
    { 'ai/custom/skills/app-check/SKILL.md': '---\nname: app-check\n---\n' },
    { 'ai/custom/skills/app-check/SKILL.md': '---\nname: app-check\ndescription: Not closed.\n' },
    { 'ai/custom/skills/app-check/SKILL.md': '---\nname: app-check\n---\ndescription: Outside metadata.\n' },
  ]) assert.throws(() => customAdapters(invalid))
  for (const change of [
    value => { value.schemaVersion = 2 },
    value => { value.files.push({ path: '.env', role: 'file', sha256: digest('secret') }) },
    value => { value.custom = null },
    value => { value.custom[0].path = 'Program.cs' },
    value => { value.custom[0].content = 'changed' },
  ]) {
    const invalid = structuredClone(manifest)
    change(invalid)
    assert.throws(() => validateInstalled(invalid, baseline))
  }
  const current = { ...installedFiles(baseline), ...Object.fromEntries(Object.entries(adapters).map(([path, file]) => [path, file.content])) }
  const plan = planKit({ baseline, incoming: baseline, installed: manifest, current })
  assert.equal(materializePlan(plan, current)['.agents/skills/app-check/SKILL.md'], null)
})
