#!/usr/bin/env node

import { execFileSync } from 'node:child_process'
import { readFile } from 'node:fs/promises'
import { resolve } from 'node:path'

const options = parseArguments(process.argv.slice(2))
const repositoryRoot = resolve(import.meta.dirname, '../..')
const coverage = parseLcov(await readFile(options.report, 'utf8'))
const changed = parseChangedLines(execFileSync(
  'git',
  ['diff', '--unified=0', '--no-color', options.baseRef, '--', 'ui/**/*.ts', 'ui/**/*.vue'],
  { cwd: repositoryRoot, encoding: 'utf8' },
))
const failures = []

for (const [file, changedLines] of changed) {
  if (!isFeatureFile(file)) continue
  const details = resolveCoverage(coverage, file)
  if (!details) {
    failures.push(`${file}: changed production file is absent from LCOV`)
    continue
  }

  for (const line of changedLines) {
    const hits = details.lines.get(line)
    if (hits === 0) failures.push(`${file}:${line}: uncovered line`)
    for (const branchHits of details.branches.get(line) ?? []) {
      if (branchHits === 0) failures.push(`${file}:${line}: uncovered branch`)
    }
  }
}

if (failures.length > 0) {
  console.error('Frontend diff coverage gate failed:')
  for (const failure of failures) console.error(`- ${failure}`)
  process.exit(1)
}

console.log(`Frontend diff coverage gate passed against ${options.baseRef}.`)

function parseLcov(lcov) {
  const result = new Map()
  let current = null
  for (const line of lcov.split('\n')) {
    if (line.startsWith('SF:')) {
      current = {
        file: normalize(line.slice(3)),
        lines: new Map(),
        branches: new Map(),
      }
      result.set(current.file, current)
    } else if (current && line.startsWith('DA:')) {
      const [number, hits] = line.slice(3).split(',').map(Number)
      if (Number.isInteger(number) && Number.isFinite(hits)) current.lines.set(number, hits)
    } else if (current && line.startsWith('BRDA:')) {
      const [number, , , rawHits] = line.slice(5).split(',')
      const parsedLine = Number(number)
      const hits = rawHits === '-' ? 0 : Number(rawHits)
      if (!Number.isInteger(parsedLine) || !Number.isFinite(hits)) continue
      const branches = current.branches.get(parsedLine) ?? []
      branches.push(hits)
      current.branches.set(parsedLine, branches)
    } else if (line === 'end_of_record') {
      current = null
    }
  }
  return result
}

function parseChangedLines(diff) {
  const result = new Map()
  let current = null
  for (const line of diff.split('\n')) {
    if (line.startsWith('+++ b/')) {
      current = normalize(line.slice(6))
      if (!result.has(current)) result.set(current, new Set())
      continue
    }
    if (!current || !line.startsWith('@@')) continue
    const match = line.match(/\+(\d+)(?:,(\d+))?/)
    if (!match) continue
    const start = Number(match[1])
    const count = match[2] === undefined ? 1 : Number(match[2])
    for (let offset = 0; offset < count; offset += 1) result.get(current).add(start + offset)
  }
  return result
}

function resolveCoverage(coverage, expected) {
  const candidates = new Set([
    normalize(expected),
    normalize(expected).replace(/^ui\//, ''),
  ])
  for (const [file, details] of coverage) {
    if (Array.from(candidates).some(
      (candidate) => file === candidate || file.endsWith(`/${candidate}`),
    )) return details
  }
  return null
}

function isFeatureFile(file) {
  return [
    'ui/ngb-ui-framework/src/ngb/api/contracts.ts',
    'ui/ngb-ui-framework/src/ngb/api/documents.ts',
    'ui/ngb-ui-framework/src/ngb/editor/config.ts',
    'ui/ngb-ui-framework/src/ngb/editor/useConfiguredEntityEditorDocumentActions.ts',
    'ui/ngb-ui-framework/src/ngb/site/NgbSiteShell.vue',
    'ui/ngb-ui-framework/src/ngb/site/NgbTopBar.vue',
    'ui/ngb-agency-billing-web/src/editor/',
    'ui/ngb-agency-billing-web/src/router/router.ts',
    'ui/ngb-property-management-web/src/editor/',
    'ui/ngb-property-management-web/src/router/router.ts',
    'ui/ngb-trade-web/src/editor/',
    'ui/ngb-trade-web/src/router/router.ts',
    'ui/ngb-crm-web/src/editor/',
    'ui/ngb-crm-web/src/router/router.ts',
  ].some((prefix) => file === prefix || file.startsWith(prefix))
}

function parseArguments(args) {
  return {
    report: readOption(args, '--report'),
    baseRef: readOption(args, '--base-ref'),
  }
}

function readOption(args, name) {
  const index = args.indexOf(name)
  const value = index >= 0 ? args[index + 1]?.trim() : ''
  if (!value) throw new Error(`Missing required option ${name}.`)
  return value
}

function normalize(value) {
  return value.replaceAll('\\', '/').replace(/^\.?\//, '')
}
