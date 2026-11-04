import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { blockEnd, blockStart, digest, objectKeys, validatePayload } from '../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { applyAssets, assetPaths, readAsset } from '../packaging/templates/content/infrastructure/ngb/kit-files.mjs'
import { materializePlan, planKit } from '../packaging/templates/content/infrastructure/ngb/kit-plan.mjs'
import { verifyGuidance } from './guidance.mjs'
import { verifyCapabilities } from './capabilities.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const template = resolve(repository, 'packaging/templates/content')
const payloadPath = 'infrastructure/ngb/kit-payload.json'

export async function preparePayload(root = repository) {
  const source = resolve(root, 'ai-devkit')
  const metadata = JSON.parse(await readFile(resolve(source, 'kit-source.json'), 'utf8'))
  objectKeys(metadata, ['schemaVersion', 'version'])
  assert.equal(metadata.schemaVersion, 1, 'Unsupported canonical Kit schema.')
  const files = []
  const add = (path, content, role = 'file') => files.push({ path, role, content, sha256: digest(content) })
  add('ai/vendor/instructions.md', await readFile(resolve(source, 'instructions.md'), 'utf8'))
  add('ai/vendor/known-limitations.json', await readFile(resolve(source, 'known-limitations.json'), 'utf8'))
  add('ai/vendor/capability-registry.json', await readFile(resolve(source, 'capability-registry.json'), 'utf8'))
  for (const path of await assetPaths(source, 'references')) {
    add(`ai/vendor/${path}`, await readAsset(source, path))
  }
  for (const path of await assetPaths(source, 'examples')) {
    assert.ok(!/(?:^|\/)(?:bin|obj|node_modules|TestResults)(?:\/|$)/.test(path),
      'Compile reference examples in an isolated copy; generated build outputs cannot enter the Kit.')
    add(`ai/vendor/${path}`, await readAsset(source, path))
  }
  for (const path of await assetPaths(source, 'skills')) {
    const content = await readAsset(source, path)
    if (path.endsWith('/SKILL.md')) {
      const name = path.split('/')[1]
      assert.match(name, /^ngb-[a-z0-9-]+$/)
      assert.ok(content.startsWith(`---\nname: ${name}\n`), `Skill name differs from its directory: ${path}`)
      assert.match(content, /\ndescription: [^\n]+\n/)
    }
    add(`ai/vendor/${path}`, content.replaceAll('{{references}}', '../../references'))
    for (const agent of ['agents', 'claude']) {
      add(`.${agent}/${path}`, content.replaceAll('{{references}}', '../../../ai/vendor/references'))
    }
  }
  add('AGENTS.md', [
    blockStart,
    'Resolve paths below from the directory containing this AGENTS.md and ngb.config.json.',
    'When starting in a subdirectory, use that ancestor as the application root.',
    'Read ai/vendor/instructions.md and ai/custom/instructions.md before working on the application.',
    'Read installed SDK and Kit versions from the application root with:',
    '`node -p \'JSON.stringify(require("./ai/kit.json").versions)\'`',
    'Do not print the complete ownership manifest.',
    "Use the applicable ngb-* skill and the application's own infrastructure/ngb.mjs.",
    blockEnd,
  ].join('\n'), 'block')
  add('CLAUDE.md', `${blockStart}\n@AGENTS.md\n${blockEnd}`, 'block')
  files.sort((left, right) => left.path.localeCompare(right.path, 'en'))
  const payload = validatePayload({ schemaVersion: 1, version: metadata.version,
    source: { package: 'NGB.Platform.Templates', version: metadata.version }, files })
  verifyGuidance(payload)
  inspectCapabilities(payload)
  return payload
}

export function inspectCapabilities(payload, evidence = null) {
  const files = new Map(payload.files.filter(file => file.path.startsWith('ai/vendor/'))
    .map(file => [file.path.slice('ai/vendor/'.length), file.content]))
  const index = JSON.parse(files.get('references/contracts/index.json'))
  for (const [path, sha256] of Object.entries(index.files)) {
    assert.equal(digest(files.get(path)), sha256, `Contract reference integrity differs: ${path}`)
  }
  const snapshots = index.packages.map(item => JSON.parse(files.get(item.reference)))
  snapshots.push(JSON.parse(files.get(index.ui)))
  return verifyCapabilities(JSON.parse(files.get('capability-registry.json')), snapshots, files, evidence)
}

export async function prepareTemplate(write = false) {
  const incoming = await preparePayload()
  const baselineText = await readAsset(template, payloadPath, 20 * 1024 * 1024)
  const baseline = baselineText === null ? null : validatePayload(JSON.parse(baselineText))
  const manifest = await readAsset(template, 'ai/kit.json')
  const paths = new Set([...(baseline?.files ?? []).map(file => file.path), ...incoming.files.map(file => file.path)])
  const current = { 'ai/kit.json': manifest, [payloadPath]: baselineText }
  const cliBaselinePath = 'infrastructure/ngb/baseline-3-3-0.json'
  const cliBaseline = await readFile(resolve(repository, 'ai-devkit/baselines/cli-3.3.0.json'), 'utf8')
  current[cliBaselinePath] = await readAsset(template, cliBaselinePath)
  for (const path of paths) current[path] = await readAsset(template, path)
  const plan = planKit({ baseline, incoming, installed: manifest === null ? null : JSON.parse(manifest), current })
  assert.ok(plan.entries.every(entry => ['update', 'unchanged', 'converged'].includes(entry.state)),
    'Generated template assets were edited directly. Preserve those changes in canonical ai-devkit sources before regeneration.')
  const changes = materializePlan(plan, current)
  const payloadText = `${JSON.stringify(incoming, null, 2)}\n`
  if (payloadText !== baselineText) changes[payloadPath] = payloadText
  if (current[cliBaselinePath] !== cliBaseline) changes[cliBaselinePath] = cliBaseline
  if (!write) {
    assert.deepEqual(Object.keys(changes), [], `Kit/template drift: ${Object.keys(changes).join(', ')}. Run node ai-devkit/prepare.mjs --write.`)
  } else if (Object.keys(changes).length > 0) {
    await applyAssets(template, current, changes, plan.sha256)
  }
  return { version: incoming.version, files: incoming.files.length, changed: Object.keys(changes).length }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  assert.ok(process.argv.slice(2).every(argument => argument === '--write'), 'Only --write is supported.')
  console.log(JSON.stringify(await prepareTemplate(process.argv.includes('--write'))))
}
