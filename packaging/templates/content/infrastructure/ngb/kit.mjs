import assert from 'node:assert/strict'
import { dirname, resolve, basename } from 'node:path'
import { fileURLToPath } from 'node:url'
import { digest, resolveConflicts, validatePayload } from './kit-contracts.mjs'
import { applyAssets, assetPaths, readAsset } from './kit-files.mjs'
import { customAdapters, materializePlan, planKit, planSummary, validateInstalled } from './kit-plan.mjs'
import { exactVersion, toolVersion } from './contracts.mjs'
import { platformVersion } from './project-data.mjs'

const tooling = dirname(fileURLToPath(import.meta.url))

export async function bundledPayload() {
  const text = await readAsset(tooling, 'kit-payload.json', 20 * 1024 * 1024)
  assert.ok(text, 'The CLI package is missing its verified Kit payload. Reinstall the exact official CLI package.')
  return validatePayload(JSON.parse(text))
}

export async function sdkVersion(root) {
  const props = await readAsset(root, 'Directory.Build.props')
  assert.ok(props, 'Directory.Build.props is missing.')
  return exactVersion(platformVersion(props))
}

export function report(command, checks, details = {}) {
  const passed = checks.every(check => !check.required || ['passed', 'not-applicable'].includes(check.state))
  return { schemaVersion: 1, command, status: passed ? 'passed' : 'failed', cliVersion: toolVersion,
    ...details, checks }
}

export function emitReport(value, format) {
  if (format === 'json') console.log(JSON.stringify(value, null, 2))
  else {
    console.log(`NGB ${value.command}: ${value.status}`)
    for (const check of value.checks) console.log(`  ${check.state}: ${check.id} — ${check.message}`)
    if (value.plan) {
      console.log(`  Plan: ${value.plan.planSha256}`)
      for (const entry of value.plan.entries) console.log(`  ${entry.state}: ${entry.path}`)
      if (value.plan.entries.some(entry => entry.content)) {
        console.log('  Use --format json to review conflict contents and their exact hashes.')
      }
    }
    if (value.receipt) console.log(`  Recovery receipt: .ngb/transactions/${value.receipt.id}/receipt.json`)
    if (value.recovery) {
      for (const path of value.recovery.files) console.log(`  Recovery file: ${path}`)
    }
  }
  if (value.status !== 'passed') {
    process.exitCode = value.checks.some(check => check.id === 'kit.conflicts' && check.state === 'failed') ? 4 : 2
  }
}

export async function inspectKit(root, payload = null) {
  const incoming = payload ?? await bundledPayload()
  const manifestText = await readAsset(root, 'ai/kit.json', 16 * 1024 * 1024)
  const installed = manifestText === null ? null : JSON.parse(manifestText)
  if (installed) validateInstalled(installed, incoming)
  const sources = {}
  let customBytes = 0
  for (const path of await assetPaths(root, 'ai/custom/skills')) {
    const text = await readAsset(root, path)
    customBytes += Buffer.byteLength(text)
    assert.ok(customBytes <= 4 * 1024 * 1024, 'Custom skill sources exceed the 4 MiB aggregate limit.')
    sources[path] = text
  }
  const adapters = customAdapters(sources)
  const paths = new Set([
    ...incoming.files.map(file => file.path),
    ...(installed?.custom ?? []).map(file => file.path),
    ...Object.keys(adapters),
  ])
  const current = { 'ai/kit.json': manifestText, ...sources }
  let currentBytes = Buffer.byteLength(manifestText ?? '') + customBytes
  for (const path of paths) {
    const text = await readAsset(root, path)
    currentBytes += Buffer.byteLength(text ?? '')
    assert.ok(currentBytes <= 64 * 1024 * 1024, 'Installed Kit assets exceed the 64 MiB inspection limit.')
    current[path] = text
  }
  const plan = planKit({ baseline: installed ? incoming : null, incoming, installed, current, customSources: sources })
  return { payload: incoming, installed, current, plan, sources }
}

export async function runKit(root, options) {
  const version = await sdkVersion(root)
  const state = await inspectKit(root)
  assert.equal(options.to ?? version, version, 'A different Kit version requires the coordinated upgrade --to <exact-version>.')
  const aligned = version === state.payload.version && version === toolVersion
  const checks = [{ id: 'kit.versions', required: true, state: aligned ? 'passed' : 'failed',
    message: aligned ? `SDK/UI/CLI/Kit target ${version}.` : 'Use matching SDK/UI/CLI/Kit versions through the coordinated upgrade.' }]
  const webText = await readAsset(root, 'web/package.json')
  checks.push({ id: 'kit.ui-version', required: true,
    state: webText && JSON.parse(webText).dependencies?.['@ngbplatform/ui'] === version ? 'passed' : 'failed',
    message: 'Frontend must declare the same exact @ngbplatform/ui version as the SDK.' })
  let plan = state.plan
  if (options.resolutions) {
    const path = resolve(options.resolutions)
    const text = await readAsset(dirname(path), basename(path))
    assert.ok(text, 'Resolution file is missing.')
    plan = resolveConflicts(plan, JSON.parse(text))
  }
  const conflicts = plan.entries.filter(entry => ['conflict', 'deleted'].includes(entry.state))
  checks.push({ id: 'kit.conflicts', required: true, state: conflicts.length ? 'failed' : 'passed',
    message: `${conflicts.length} unresolved ownership conflicts.` })
  if (options.name === 'status') {
    const modified = plan.entries.filter(entry => entry.state !== 'unchanged')
    checks.push({ id: 'kit.integrity', required: true,
      state: state.installed && modified.length === 0 ? 'passed' : 'failed',
      message: state.installed ? `${modified.length} changed or missing managed assets.` : 'Kit is not installed.' })
  }
  let receipt
  if (options.apply && checks.every(check => check.state === 'passed')) {
    const changes = materializePlan(plan, state.current)
    if (Object.keys(changes).length > 0) receipt = await applyAssets(root, state.current, changes, plan.sha256)
  }
  const customized = plan.entries.filter(entry => entry.state === 'customized'
    || entry.state === 'resolved' && entry.result !== entry.incoming)
  checks.push({ id: 'kit.customizations', required: true, state: customized.length ? 'not-run' : 'passed',
    message: customized.length ? 'Preserved customizations require review; official conformance has not been established.'
      : 'Managed guidance agrees with its canonical content.' })
  return report(`ai ${options.name}`, checks, { sdkVersion: version, kitVersion: state.payload.version,
    plan: planSummary(plan), ...(receipt ? { receipt } : {}),
    sourceSha256: digest(JSON.stringify(state.current)) })
}
