#!/usr/bin/env node

import { readFile, rm, mkdir } from 'node:fs/promises'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import libCoverage from 'istanbul-lib-coverage'
import libReport from 'istanbul-lib-report'
import reports from 'istanbul-reports'

if (process.argv[1] && fileURLToPath(import.meta.url) === resolve(process.argv[1])) {
  await main()
}

async function main() {
  const options = parseArguments(process.argv.slice(2))
  const inputReports = await Promise.all(options.inputs.map(async (input) =>
    JSON.parse(await readFile(resolve(input), 'utf8'))))
  const coverageMap = mergeFrontendCoverage(inputReports, options.inputs)

  const output = resolve(options.output)
  await rm(output, { recursive: true, force: true })
  await mkdir(output, { recursive: true })
  const context = libReport.createContext({ coverageMap, dir: output })
  for (const [name, reporterOptions] of [
    ['text-summary', {}],
    ['html', {}],
    ['json', { file: 'coverage-final.json' }],
    ['json-summary', { file: 'coverage-summary.json' }],
    ['lcovonly', { file: 'lcov.info' }],
  ]) {
    reports.create(name, reporterOptions).execute(context)
  }
}

export function mergeFrontendCoverage(inputReports, inputNames = []) {
  const variantsByFile = new Map()
  for (const [reportIndex, report] of inputReports.entries()) {
    for (const [file, coverage] of Object.entries(report)) {
      if (!variantsByFile.has(file)) variantsByFile.set(file, [])
      variantsByFile.get(file).push({
        coverage,
        inputName: inputNames[reportIndex],
        belongsToOwningPackage: belongsToOwningPackage(file, inputNames[reportIndex]),
      })
    }
  }

  const coverageMap = libCoverage.createCoverageMap({})
  for (const [file, entries] of variantsByFile) {
    const owningEntries = entries.filter((entry) => entry.belongsToOwningPackage)
    const variants = owningEntries.length > 0 ? owningEntries : entries
    const coveredVariants = variants.filter((entry) => coveredPointCount(entry.coverage) > 0)
    const candidates = coveredVariants.length > 0 ? coveredVariants : variants
    const ordered = [...candidates].sort((left, right) =>
      canonicalVariantPreference(file, right.inputName) - canonicalVariantPreference(file, left.inputName)
      || coveredPointCount(right.coverage) - coveredPointCount(left.coverage)
      || coveragePointCount(right.coverage) - coveragePointCount(left.coverage))
    const canonical = structuredClone(ordered[0].coverage)
    normalizeCoverageHits(canonical)
    for (const variant of ordered.slice(1)) mergeFileCoverageBySourceLocation(canonical, variant.coverage, {
      ignoreAbsentUncoveredPoints: true,
    })
    coverageMap.addFileCoverage(canonical)
  }
  return coverageMap
}

function canonicalVariantPreference(file, inputName) {
  const projectName = String(inputName ?? '').replaceAll('\\', '/').split('/').at(-2) ?? ''
  if (String(file).endsWith('.vue')) return projectName.endsWith('-browser') ? 1 : 0
  return projectName.endsWith('-unit') ? 1 : 0
}

function belongsToOwningPackage(file, inputName) {
  if (!inputName) return false
  const normalizedFile = String(file).replaceAll('\\', '/')
  const sourceMarker = '/src/'
  const sourceIndex = normalizedFile.lastIndexOf(sourceMarker)
  if (sourceIndex < 0) return false

  const packageName = normalizedFile.slice(0, sourceIndex).split('/').pop()
  if (!packageName) return false

  const normalizedInput = resolve(inputName).replaceAll('\\', '/')
  const projectName = normalizedInput.split('/').at(-2) ?? ''
  return projectName === `${packageName}-unit` || projectName === `${packageName}-browser`
}

function coveragePointCount(coverage) {
  return Object.keys(coverage.s ?? {}).length
    + Object.keys(coverage.f ?? {}).length
    + Object.values(coverage.b ?? {}).reduce((count, values) => count + values.length, 0)
}

function coveredPointCount(coverage) {
  return Object.values(coverage.s ?? {}).filter((hits) => hits > 0).length
    + Object.values(coverage.f ?? {}).filter((hits) => hits > 0).length
    + Object.values(coverage.b ?? {}).flat().filter((hits) => hits > 0).length
}

function mergeFileCoverageBySourceLocation(target, source, options = {}) {
  mergeScalarMetric(target.statementMap, target.s, source.statementMap, source.s, statementBaseKey, options)
  mergeScalarMetric(target.fnMap, target.f, source.fnMap, source.f, functionBaseKey, options)
  mergeBranchMetric(target.branchMap, target.b, source.branchMap, source.b, options)
}

function mergeScalarMetric(targetMap, targetHits, sourceMap, sourceHits, baseKey, options) {
  const targetIndex = semanticIndex(targetMap, baseKey)
  const sourceIndex = semanticIndex(sourceMap, baseKey)
  for (const [semanticKey, sourceId] of sourceIndex) {
    const targetId = targetIndex.get(semanticKey)
    if (targetId !== undefined) {
      targetHits[targetId] = normalizedHitCount(targetHits[targetId]) + normalizedHitCount(sourceHits[sourceId])
    } else {
      if (options.ignoreAbsentUncoveredPoints && normalizedHitCount(sourceHits[sourceId]) === 0) continue
      const nextId = nextMetricId(targetMap)
      targetMap[nextId] = structuredClone(sourceMap[sourceId])
      targetHits[nextId] = normalizedHitCount(sourceHits[sourceId])
    }
  }
}

function mergeBranchMetric(targetMap, targetHits, sourceMap, sourceHits, options) {
  const targetIndex = semanticIndex(targetMap, branchBaseKey)
  const sourceIndex = semanticIndex(sourceMap, branchBaseKey)
  for (const [semanticKey, sourceId] of sourceIndex) {
    const targetId = targetIndex.get(semanticKey)
    if (targetId !== undefined) {
      const sourceValues = sourceHits[sourceId] ?? []
      const targetValues = targetHits[targetId] ?? []
      for (let index = 0; index < sourceValues.length; index += 1) {
        if (index < targetValues.length) {
          targetValues[index] = normalizedHitCount(targetValues[index]) + normalizedHitCount(sourceValues[index])
        } else {
          targetValues.push(normalizedHitCount(sourceValues[index]))
          const sourceLocation = sourceMap[sourceId]?.locations?.[index]
          if (sourceLocation) targetMap[targetId].locations.push(structuredClone(sourceLocation))
        }
      }
    } else {
      if (options.ignoreAbsentUncoveredPoints
        && (sourceHits[sourceId] ?? []).every((hits) => normalizedHitCount(hits) === 0)) continue
      const nextId = nextMetricId(targetMap)
      targetMap[nextId] = structuredClone(sourceMap[sourceId])
      targetHits[nextId] = (sourceHits[sourceId] ?? []).map(normalizedHitCount)
    }
  }
}

function normalizeCoverageHits(coverage) {
  for (const [id, hits] of Object.entries(coverage.s ?? {})) coverage.s[id] = normalizedHitCount(hits)
  for (const [id, hits] of Object.entries(coverage.f ?? {})) coverage.f[id] = normalizedHitCount(hits)
  for (const [id, hits] of Object.entries(coverage.b ?? {})) coverage.b[id] = hits.map(normalizedHitCount)
}

function normalizedHitCount(value) {
  return Math.max(0, Number(value ?? 0))
}

function semanticIndex(map, baseKey) {
  const grouped = new Map()
  for (const [id, value] of Object.entries(map ?? {})) {
    const base = baseKey(value)
    if (!grouped.has(base)) grouped.set(base, [])
    grouped.get(base).push([id, value])
  }

  const index = new Map()
  for (const [base, entries] of grouped) {
    entries.sort((left, right) => compareLocations(metricLocation(left[1]), metricLocation(right[1])))
    entries.forEach(([id], ordinal) => index.set(`${base}:${ordinal}`, id))
  }
  return index
}

function statementBaseKey(value) {
  const location = metricLocation(value)
  return `statement:${location.end.line ?? location.start.line}`
}

function functionBaseKey(value) {
  const declarationLine = value.decl?.start?.line ?? value.line ?? metricLocation(value).start.line
  const name = /^\(anonymous_\d+\)$/.test(value.name ?? '') ? '(anonymous)' : value.name ?? ''
  return `function:${declarationLine}:${name}`
}

function branchBaseKey(value) {
  const location = metricLocation(value)
  return `branch:${location.end.line ?? value.line ?? location.start.line}:${value.type ?? ''}`
}

function metricLocation(value) {
  return value.loc ?? value
}

function compareLocations(left, right) {
  return left.start.line - right.start.line
    || (left.start.column ?? -1) - (right.start.column ?? -1)
    || left.end.line - right.end.line
    || (left.end.column ?? -1) - (right.end.column ?? -1)
}

function nextMetricId(map) {
  const ids = Object.keys(map).map(Number).filter(Number.isFinite)
  return String(ids.length === 0 ? 0 : Math.max(...ids) + 1)
}

function parseArguments(args) {
  const inputs = []
  let output = ''
  for (let index = 0; index < args.length; index += 1) {
    if (args[index] === '--input') inputs.push(args[++index]?.trim())
    else if (args[index] === '--output') output = args[++index]?.trim()
    else throw new Error(`Unknown option: ${args[index]}`)
  }
  if (inputs.length === 0 || inputs.some((input) => !input)) {
    throw new Error('At least one non-empty --input is required.')
  }
  if (!output) throw new Error('--output is required.')
  return { inputs, output }
}
