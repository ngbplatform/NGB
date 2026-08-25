#!/usr/bin/env node

import { access, readFile } from 'node:fs/promises'
import { resolve } from 'node:path'

const workspaceRoot = resolve(process.argv[2] ?? '.')
const requestedKind = process.argv[3] ?? 'all'
if (!['all', 'e2e', 'vitest'].includes(requestedKind)) {
  throw new Error(`Expected discovery kind all, vitest, or e2e; received: ${requestedKind}`)
}

const manifest = JSON.parse(await readFile(resolve(workspaceRoot, 'package.json'), 'utf8'))
if (!Array.isArray(manifest.workspaces) || manifest.workspaces.length === 0) {
  throw new Error(`No npm workspaces were found in ${workspaceRoot}.`)
}

for (const workspace of manifest.workspaces) {
  if (typeof workspace !== 'string' || workspace.includes('*') || /[|\r\n]/u.test(workspace)) {
    throw new Error(`Coverage discovery requires an explicit safe workspace path; received: ${workspace}`)
  }
  if (requestedKind !== 'e2e') {
    for (const [config, kind] of [['vitest.config.ts', 'unit'], ['vitest.browser.config.ts', 'browser']]) {
      if (await exists(resolve(workspaceRoot, workspace, config))) console.log(`${workspace}|${config}|${kind}`)
    }
  }
  if (requestedKind !== 'vitest' && await exists(resolve(workspaceRoot, workspace, 'playwright.config.ts'))) {
    console.log(`${workspace}|playwright.config.ts|e2e`)
  }
}

async function exists(path) {
  try {
    await access(path)
    return true
  } catch (error) {
    if (error?.code === 'ENOENT') return false
    throw error
  }
}
