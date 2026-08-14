#!/usr/bin/env node

import { execFileSync } from 'node:child_process'
import { readFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const repositoryRoot = resolve(import.meta.dirname, '../..')
const declarationOnlyFeatureFiles = new Map([
  ['NGB.Contracts/Documents/DocumentActionContractEnums.cs', 'enum'],
  ['NGB.Contracts/Documents/StandardDocumentTargets.cs', 'constants'],
  ['NGB.Contracts/WorkCenter/WorkCenterContractEnums.cs', 'enum'],
  ['NGB.Core/Documents/Actions/DocumentActionContextKeys.cs', 'constants'],
  ['NGB.Core/Documents/Actions/DocumentActionEnums.cs', 'enum'],
  ['NGB.Core/WorkCenter/WorkCenterEnums.cs', 'enum'],
  ['NGB.Definitions/Documents/Actions/IDocumentActionDefinitionsContributor.cs', 'interface'],
])

if (resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url)) {
  await main()
}

async function main() {
  const options = parseArguments(process.argv.slice(2))
  const xml = await readFile(options.report, 'utf8')
  const coverageByFile = parseCoverage(xml)
  const changedLines = parseChangedLines(execFileSync(
    'git',
    ['diff', '--unified=0', '--no-color', options.baseRef, '--', ':(glob)**/*.cs'],
    { cwd: repositoryRoot, encoding: 'utf8' },
  ))
  const failures = []

  for (const [file, lines] of changedLines) {
    if (!isProductionFeatureFile(file)) continue
    const coverage = resolveFileCoverage(coverageByFile, file)
    if (!coverage) {
      if (await isVerifiedDeclarationOnlyFeatureFile(file, repositoryRoot)) continue
      failures.push(`${file}: changed production file is absent from the merged coverage report`)
      continue
    }

    for (const line of lines) {
      const detail = coverage.get(line)
      if (!detail) continue
      if (detail.hits === 0) failures.push(`${file}:${line}: uncovered line`)
      if (detail.branchRate !== null && detail.branchRate < 1) {
        failures.push(`${file}:${line}: branch coverage ${(detail.branchRate * 100).toFixed(2)}%`)
      }
    }
  }

  if (failures.length > 0) {
    console.error('Backend diff coverage gate failed:')
    for (const failure of failures) console.error(`- ${failure}`)
    process.exitCode = 1
    return
  }

  console.log(`Backend diff coverage gate passed against ${options.baseRef}.`)
}

async function isVerifiedDeclarationOnlyFeatureFile(file, root) {
  const kind = declarationOnlyFeatureFiles.get(normalizePath(file))
  if (!kind) return false

  const source = await readFile(resolve(root, file), 'utf8')
  return isDeclarationOnlySource(source, kind)
}

function isDeclarationOnlySource(source, kind) {
  let remaining = stripComments(source)
    .replace(/^\s*using\s+[\w.]+\s*;\s*$/gm, '')
    .replace(/^\s*namespace\s+[\w.]+\s*;\s*$/gm, '')

  if (kind === 'enum') {
    remaining = remaining.replace(
      /\b(?:public|internal)\s+enum\s+\w+(?:\s*:\s*[\w.]+)?\s*\{([^{}]*)\}/g,
      (declaration, body) => isEnumBody(body) ? '' : declaration,
    )
  } else if (kind === 'constants') {
    remaining = remaining.replace(
      /\b(?:public|internal)\s+static\s+class\s+\w+\s*\{([^{}]*)\}/g,
      (declaration, body) => isConstantsOnlyBody(body) ? '' : declaration,
    )
  } else if (kind === 'interface') {
    remaining = remaining.replace(
      /\b(?:public|internal)\s+interface\s+\w+(?:\s*:\s*[^{}]+)?\s*\{([^{}]*)\}/g,
      (declaration, body) => isInterfaceSignaturesOnlyBody(body) ? '' : declaration,
    )
  } else {
    return false
  }

  return remaining.trim().length === 0
}

function stripComments(source) {
  return source
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/^\s*\/\/\/.*$/gm, '')
    .replace(/^\s*\/\/.*$/gm, '')
}

function isEnumBody(body) {
  return body
    .split(',')
    .map((entry) => entry.trim())
    .filter(Boolean)
    .every((entry) => /^\w+(?:\s*=\s*[-+]?\d+)?$/.test(entry))
}

function isConstantsOnlyBody(body) {
  const members = body
    .split(';')
    .map((member) => member.trim())
    .filter(Boolean)

  return members.length > 0 && members.every((member) =>
    /^(?:public|internal|private)\s+const\s+[\w.<>?]+\s+\w+\s*=\s*(?:"(?:[^"\\]|\\.)*"|[-+]?\d+(?:\.\d+)?|true|false|null)$/.test(member),
  )
}

function isInterfaceSignaturesOnlyBody(body) {
  if (body.includes('=>') || body.includes('=')) return false
  const members = body
    .split(';')
    .map((member) => member.trim())
    .filter(Boolean)

  return members.length > 0 && members.every((member) =>
    /^(?:[\w.<>?,\[\]\s]+)\s+\w+\s*\([^{};]*\)$/.test(member),
  )
}

function parseArguments(args) {
  return {
    report: readOption(args, '--report'),
    baseRef: readOption(args, '--base-ref'),
  }
}

function parseCoverage(xml) {
  const files = new Map()
  for (const classMatch of xml.matchAll(/<class\b([^>]*)>([\s\S]*?)<\/class>/g)) {
    const attributes = parseAttributes(classMatch[1] ?? '')
    const filename = normalizePath(decodeXml(attributes.filename ?? ''))
    if (!filename) continue
    const lines = files.get(filename) ?? new Map()
    for (const lineMatch of (classMatch[2] ?? '').matchAll(/<line\b([^>]*)\/?>/g)) {
      const lineAttributes = parseAttributes(lineMatch[1] ?? '')
      const number = Number(lineAttributes.number)
      const hits = Number(lineAttributes.hits)
      if (!Number.isInteger(number) || !Number.isFinite(hits)) continue
      lines.set(number, {
        hits,
        branchRate: parseBranchRate(lineAttributes['condition-coverage']),
      })
    }
    files.set(filename, lines)
  }
  return files
}

function parseChangedLines(diff) {
  const files = new Map()
  let currentFile = null
  for (const line of diff.split('\n')) {
    if (line.startsWith('+++ b/')) {
      currentFile = normalizePath(line.slice(6))
      if (!files.has(currentFile)) files.set(currentFile, new Set())
      continue
    }
    if (!currentFile || !line.startsWith('@@')) continue
    const match = line.match(/\+(\d+)(?:,(\d+))?/)
    if (!match) continue
    const start = Number(match[1])
    const count = match[2] === undefined ? 1 : Number(match[2])
    for (let offset = 0; offset < count; offset += 1) {
      files.get(currentFile).add(start + offset)
    }
  }
  return files
}

function resolveFileCoverage(files, expected) {
  const normalized = normalizePath(expected)
  for (const [filename, coverage] of files) {
    if (filename === normalized || filename.endsWith(`/${normalized}`)) return coverage
  }
  return null
}

function isProductionFeatureFile(file) {
  if (file.includes('/bin/') || file.includes('/obj/') || file.includes('Tests/')) return false
  return [
    'NGB.Api/Controllers/DocumentControllerBase.cs',
    'NGB.Api/Controllers/WorkCenterControllerBase.cs',
    'NGB.Api/WorkCenter/',
    'NGB.Core/Documents/Actions/',
    'NGB.Core/Events/',
    'NGB.Core/WorkCenter/',
    'NGB.Contracts/Documents/',
    'NGB.Contracts/WorkCenter/',
    'NGB.Definitions/Documents/Actions/',
    'NGB.Metadata/Documents/Actions/',
    'NGB.Persistence/Documents/Actions/',
    'NGB.Persistence/Outbox/',
    'NGB.Persistence/WorkCenter/',
    'NGB.PostgreSql/Documents/Actions/',
    'NGB.PostgreSql/Outbox/',
    'NGB.PostgreSql/WorkCenter/',
    'NGB.Runtime/Documents/Actions/',
    'NGB.Runtime/Observability/',
    'NGB.Runtime/WorkCenter/',
    'NGB.PropertyManagement.Runtime/DocumentActions/',
    'NGB.PropertyManagement.Runtime/WorkCenter/',
    'NGB.CRM.Runtime/DocumentActions/',
    'NGB.CRM.Runtime/WorkCenter/',
  ].some((prefix) => file === prefix || file.startsWith(prefix))
}

function parseBranchRate(value) {
  if (!value) return null
  const match = value.match(/\((\d+)\/(\d+)\)/)
  if (!match) return null
  const covered = Number(match[1])
  const total = Number(match[2])
  return total === 0 ? 1 : covered / total
}

function parseAttributes(value) {
  return Object.fromEntries(
    Array.from(value.matchAll(/([\w-]+)="([^"]*)"/g), (match) => [match[1], match[2]]),
  )
}

function decodeXml(value) {
  return value
    .replaceAll('&quot;', '"')
    .replaceAll('&apos;', "'")
    .replaceAll('&lt;', '<')
    .replaceAll('&gt;', '>')
    .replaceAll('&amp;', '&')
}

function normalizePath(value) {
  return value.replaceAll('\\', '/').replace(/^\.?\//, '')
}

function readOption(args, name) {
  const index = args.indexOf(name)
  const value = index >= 0 ? args[index + 1]?.trim() : ''
  if (!value) throw new Error(`Missing required option ${name}.`)
  return value
}

export {
  isDeclarationOnlySource,
  isProductionFeatureFile,
  isVerifiedDeclarationOnlyFeatureFile,
}
