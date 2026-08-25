#!/usr/bin/env node

import { createRequire } from 'node:module'
import { readdir, readFile, writeFile } from 'node:fs/promises'
import { extname, isAbsolute, relative, resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

const MAX_FAILURES_TO_PRINT = 200
const SOURCE_EXTENSIONS = new Set(['.js', '.jsx', '.ts', '.tsx', '.vue'])
const METRICS = ['lines', 'branches', 'functions', 'statements']

if (process.argv[1] && fileURLToPath(import.meta.url) === resolve(process.argv[1])) {
  await main()
}

async function main() {
  const options = parseArguments(process.argv.slice(2))
  const repositoryRoot = resolve(options.repositoryRoot)
  const workspaceRoot = resolve(repositoryRoot, options.workspace)
  const report = resolve(repositoryRoot, options.report)
  const output = resolve(repositoryRoot, options.output)

  const [summaryJson, discovery] = await Promise.all([
    readFile(report, 'utf8'),
    discoverFrontendFiles(repositoryRoot, workspaceRoot),
  ])
  const typescript = loadTypeScript(workspaceRoot)
  const requiredFiles = []
  const excludedFiles = []

  for (const source of discovery.files) {
    const contents = await readFile(resolve(repositoryRoot, source), 'utf8')
    const exclusion = classifyCoverageExclusion(source, contents, typescript)
    if (exclusion) excludedFiles.push({ file: source, reason: exclusion })
    else requiredFiles.push(source)
  }

  const reportFiles = parseVitestSummary(summaryJson, repositoryRoot)
  const evaluation = evaluateCoverage({
    requiredFiles,
    excludedFiles,
    reportFiles,
    thresholds: options.thresholds,
  })
  const result = {
    generatedAtUtc: new Date().toISOString(),
    workspace: normalizePath(relative(repositoryRoot, workspaceRoot)),
    report: normalizePath(relative(repositoryRoot, report)),
    productionPackages: discovery.productionPackages,
    testProjects: discovery.testProjects,
    e2eProjects: discovery.e2eProjects,
    thresholds: options.thresholds,
    ...evaluation,
  }

  await writeFile(output, `${JSON.stringify(result, null, 2)}\n`, 'utf8')
  printResult(result, output, repositoryRoot)
  if (!result.passed) process.exitCode = 1
}

function parseArguments(args) {
  return {
    repositoryRoot: readOption(args, '--repository-root'),
    workspace: readOption(args, '--workspace'),
    report: readOption(args, '--report'),
    output: readOption(args, '--output'),
    thresholds: Object.fromEntries(METRICS.map((metric) => [metric, readPercentage(args, `--${metric}`)])),
  }
}

function readPercentage(args, name) {
  const value = Number(readOption(args, name))
  if (!Number.isFinite(value) || value < 0 || value > 100) {
    throw new Error(`${name} must be a number from 0 through 100.`)
  }
  return value
}

function readOption(args, name) {
  const index = args.indexOf(name)
  const value = index >= 0 ? args[index + 1]?.trim() : ''
  if (!value) throw new Error(`Missing required option ${name}.`)
  return value
}

export async function discoverFrontendFiles(repositoryRoot, workspaceRoot) {
  const workspaceManifest = JSON.parse(await readFile(resolve(workspaceRoot, 'package.json'), 'utf8'))
  const workspaces = workspaceManifest.workspaces
  if (!Array.isArray(workspaces) || workspaces.length === 0) {
    throw new Error(`No frontend workspaces were found in ${workspaceRoot}.`)
  }

  const files = []
  const productionPackages = []
  const testProjects = []
  const e2eProjects = []
  for (const workspace of workspaces) {
    if (typeof workspace !== 'string' || workspace.includes('*')) {
      throw new Error(`Frontend full coverage requires explicit workspace paths; received: ${workspace}`)
    }
    const packageRoot = resolve(workspaceRoot, workspace)
    const manifest = JSON.parse(await readFile(resolve(packageRoot, 'package.json'), 'utf8'))
    productionPackages.push({
      name: String(manifest.name ?? workspace),
      path: normalizePath(relative(repositoryRoot, packageRoot)),
    })
    files.push(...await listSourceFiles(resolve(packageRoot, 'src'), repositoryRoot))

    for (const config of ['vitest.config.ts', 'vitest.browser.config.ts']) {
      if (await pathExists(resolve(packageRoot, config))) {
        testProjects.push(normalizePath(relative(repositoryRoot, resolve(packageRoot, config))))
      }
    }
    if (await pathExists(resolve(packageRoot, 'playwright.config.ts'))) {
      e2eProjects.push(normalizePath(relative(repositoryRoot, resolve(packageRoot, 'playwright.config.ts'))))
    }
  }

  if (files.length === 0) throw new Error('No frontend production source files were discovered.')
  if (testProjects.length === 0) throw new Error('No frontend Vitest projects were discovered.')

  return {
    files: [...new Set(files)].sort(),
    productionPackages: productionPackages.sort((left, right) => left.path.localeCompare(right.path)),
    testProjects: testProjects.sort(),
    e2eProjects: e2eProjects.sort(),
  }
}

async function listSourceFiles(directory, repositoryRoot) {
  const entries = await readdir(directory, { withFileTypes: true })
  const files = []
  for (const entry of entries) {
    const fullPath = resolve(directory, entry.name)
    if (entry.isDirectory()) {
      if (!['coverage', 'dist', 'node_modules'].includes(entry.name)) {
        files.push(...await listSourceFiles(fullPath, repositoryRoot))
      }
    } else if (entry.isFile() && SOURCE_EXTENSIONS.has(extname(entry.name))) {
      files.push(normalizePath(relative(repositoryRoot, fullPath)))
    }
  }
  return files
}

async function pathExists(path) {
  try {
    await readFile(path)
    return true
  } catch (error) {
    if (error?.code === 'EISDIR') return true
    if (error?.code === 'ENOENT') return false
    throw error
  }
}

function loadTypeScript(workspaceRoot) {
  const require = createRequire(resolve(workspaceRoot, 'package.json'))
  return require('typescript')
}

export function classifyCoverageExclusion(file, source, typescript = null) {
  const normalizedFile = normalizePath(file)
  if (isGeneratedBuildPath(normalizedFile)) return 'generated-build-output'
  if (isGeneratedSource(normalizedFile, source)) return 'generated-source'
  if (normalizedFile.endsWith('.d.ts')) return 'declaration-only-types'
  if (normalizedFile.endsWith('.vue')) return null

  const ts = typescript ?? loadTypeScript(resolve(process.cwd(), 'ui'))
  const scriptKind = normalizedFile.endsWith('.tsx') || normalizedFile.endsWith('.jsx')
    ? ts.ScriptKind.TSX
    : normalizedFile.endsWith('.js')
      ? ts.ScriptKind.JS
      : ts.ScriptKind.TS
  const sourceFile = ts.createSourceFile(normalizedFile, source, ts.ScriptTarget.Latest, true, scriptKind)
  if (sourceFile.parseDiagnostics.length > 0) return null

  const statements = sourceFile.statements.filter((statement) => !ts.isImportDeclaration(statement))
  if (statements.length === 0) return 'declaration-only-types-or-reexports'
  if (statements.every((statement) => isTypeDeclaration(statement, ts))) {
    return 'declaration-only-types-or-reexports'
  }
  if (isConstOnlyModule(statements, ts)) return 'declaration-only-constants'
  return null
}

function isTypeDeclaration(statement, ts) {
  if (ts.isInterfaceDeclaration(statement) || ts.isTypeAliasDeclaration(statement)) return true
  if (ts.isExportDeclaration(statement) && !statement.exportClause) return true
  if (ts.isExportDeclaration(statement)) {
    return statement.isTypeOnly
      || statement.exportClause?.elements?.every((element) => element.isTypeOnly)
      || false
  }
  if (ts.isModuleDeclaration(statement)) {
    return hasModifier(statement, ts.SyntaxKind.DeclareKeyword)
  }
  if (ts.isEnumDeclaration(statement)) return true
  if (ts.isFunctionDeclaration(statement) || ts.isClassDeclaration(statement)) {
    return hasModifier(statement, ts.SyntaxKind.DeclareKeyword)
  }
  if (ts.isVariableStatement(statement)) {
    return hasModifier(statement, ts.SyntaxKind.DeclareKeyword)
  }
  return statement.kind === ts.SyntaxKind.EmptyStatement
}

function isConstOnlyModule(statements, ts) {
  let foundConst = false
  for (const statement of statements) {
    if (isTypeDeclaration(statement, ts)) continue
    if (!ts.isVariableStatement(statement)) return false
    if ((statement.declarationList.flags & ts.NodeFlags.Const) === 0) return false
    for (const declaration of statement.declarationList.declarations) {
      foundConst = true
      if (!declaration.initializer || !isStaticValue(declaration.initializer, ts)) return false
    }
  }
  return foundConst
}

function isStaticValue(node, ts) {
  if (ts.isStringLiteral(node) || ts.isNumericLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) return true
  if ([ts.SyntaxKind.TrueKeyword, ts.SyntaxKind.FalseKeyword, ts.SyntaxKind.NullKeyword].includes(node.kind)) return true
  if (ts.isParenthesizedExpression(node) || ts.isAsExpression(node) || ts.isTypeAssertionExpression(node)
    || ts.isSatisfiesExpression?.(node)) {
    return isStaticValue(node.expression, ts)
  }
  if (ts.isPrefixUnaryExpression(node)) {
    return [ts.SyntaxKind.PlusToken, ts.SyntaxKind.MinusToken].includes(node.operator)
      && isStaticValue(node.operand, ts)
  }
  if (ts.isArrayLiteralExpression(node)) {
    return node.elements.every((element) => !ts.isSpreadElement(element) && isStaticValue(element, ts))
  }
  if (ts.isObjectLiteralExpression(node)) {
    return node.properties.every((property) => {
      if (!ts.isPropertyAssignment(property) || property.name && ts.isComputedPropertyName(property.name)) return false
      return isStaticValue(property.initializer, ts)
    })
  }
  return false
}

function hasModifier(node, kind) {
  return node.modifiers?.some((modifier) => modifier.kind === kind) ?? false
}

function isGeneratedBuildPath(file) {
  return file.split('/').some((part) => ['coverage', 'dist', 'node_modules'].includes(part))
}

function isGeneratedSource(file, source) {
  return /(?:^|\/)[^/]+\.(?:g|generated|designer)\.(?:js|jsx|ts|tsx)$/iu.test(file)
    || /<auto-generated(?:\s+\/?)?>/iu.test(source.slice(0, 2048))
    || /@generated\b/iu.test(source.slice(0, 2048))
}

export function parseVitestSummary(json, repositoryRoot) {
  const summary = JSON.parse(json)
  const files = new Map()
  for (const [filename, metrics] of Object.entries(summary)) {
    if (filename === 'total') continue
    const normalized = normalizeReportFilename(filename, repositoryRoot)
    if (!normalized || typeof metrics !== 'object' || metrics === null) continue
    files.set(normalized, Object.fromEntries(METRICS.map((metric) => [metric, normalizeMetric(metrics[metric])])))
  }
  return files
}

function normalizeReportFilename(filename, repositoryRoot) {
  const repositoryPath = isAbsolute(filename)
    ? normalizePath(relative(repositoryRoot, filename))
    : normalizePath(filename.replace(/^\.\//u, ''))
  if (repositoryPath.startsWith('../') || isGeneratedBuildPath(repositoryPath)) return null
  return repositoryPath
}

function normalizeMetric(value) {
  const covered = Number(value?.covered ?? 0)
  const total = Number(value?.total ?? 0)
  const percentage = total === 0 ? 100 : covered / total * 100
  return {
    covered: Number.isFinite(covered) ? covered : 0,
    total: Number.isFinite(total) ? total : 0,
    percentage,
  }
}

export function evaluateCoverage({ requiredFiles, excludedFiles, reportFiles, thresholds }) {
  const missingFiles = []
  const perFile = []
  const totals = emptyMetrics()

  for (const filename of requiredFiles) {
    const coverage = reportFiles.get(filename)
    if (!coverage) {
      missingFiles.push(filename)
      continue
    }
    addMetrics(totals, coverage)
    const failures = METRICS
      .filter((metric) => coverage[metric].percentage + Number.EPSILON < thresholds[metric])
      .map((metric) => `${metric} ${formatPercentage(coverage[metric].percentage)}`)
    perFile.push({ file: filename, ...coverage, failures })
  }

  const overall = finalizeMetrics(totals)
  const failingFiles = perFile.filter((file) => file.failures.length > 0)
  const overallFailures = METRICS
    .filter((metric) => overall[metric].percentage + Number.EPSILON < thresholds[metric])
    .map((metric) => `${metric} ${formatPercentage(overall[metric].percentage)}`)
  const groupedExclusions = new Map()
  for (const exclusion of excludedFiles) {
    groupedExclusions.set(exclusion.reason, (groupedExclusions.get(exclusion.reason) ?? 0) + 1)
  }
  const exclusionSummary = [...groupedExclusions.entries()]
    .map(([reason, count]) => ({ reason, count }))
    .sort((left, right) => left.reason.localeCompare(right.reason))

  return {
    passed: missingFiles.length === 0 && failingFiles.length === 0 && overallFailures.length === 0,
    sourceFiles: {
      discovered: requiredFiles.length + excludedFiles.length,
      required: requiredFiles.length,
      excluded: excludedFiles.length,
      presentInReport: perFile.length,
      missingFromReport: missingFiles.length,
    },
    overall,
    overallFailures,
    failingFileCount: failingFiles.length,
    missingFiles,
    failingFiles,
    exclusionSummary,
    excludedFiles,
  }
}

function emptyMetrics() {
  return Object.fromEntries(METRICS.map((metric) => [metric, { covered: 0, total: 0 }]))
}

function addMetrics(target, source) {
  for (const metric of METRICS) {
    target[metric].covered += source[metric].covered
    target[metric].total += source[metric].total
  }
}

function finalizeMetrics(metrics) {
  return Object.fromEntries(METRICS.map((metric) => {
    const value = metrics[metric]
    return [metric, {
      ...value,
      percentage: value.total === 0 ? 100 : value.covered / value.total * 100,
    }]
  }))
}

function printResult(result, output, repositoryRoot) {
  console.log('Frontend full coverage gate')
  console.log(`  Vitest projects:     ${result.testProjects.length}`)
  console.log(`  Playwright projects: ${result.e2eProjects.length}`)
  console.log(`  Production packages: ${result.productionPackages.length}`)
  console.log(`  Source files:        ${result.sourceFiles.discovered}`)
  console.log(`  Required files:      ${result.sourceFiles.required}`)
  console.log(`  Excluded files:      ${result.sourceFiles.excluded}`)
  console.log(`  Complete files:      ${result.sourceFiles.presentInReport}/${result.sourceFiles.required}`)
  for (const metric of METRICS) {
    const value = result.overall[metric]
    console.log(`  ${capitalize(metric).padEnd(20)}${value.covered}/${value.total} (${formatPercentage(value.percentage)})`)
  }
  if (result.exclusionSummary.length > 0) {
    console.log('  Verified exclusions:')
    for (const exclusion of result.exclusionSummary) {
      console.log(`    ${exclusion.reason}: ${exclusion.count}`)
    }
  }
  if (result.missingFiles.length > 0) {
    console.error(`Missing from coverage report (${result.missingFiles.length}):`)
    for (const file of result.missingFiles.slice(0, MAX_FAILURES_TO_PRINT)) console.error(`  ${file}`)
    printOmittedCount(result.missingFiles.length)
  }
  if (result.failingFiles.length > 0) {
    console.error(`Files below their per-file thresholds (${result.failingFiles.length}):`)
    for (const file of result.failingFiles.slice(0, MAX_FAILURES_TO_PRINT)) {
      console.error(`  ${file.file}: ${file.failures.join(', ')}`)
    }
    printOmittedCount(result.failingFiles.length)
  }
  const outputPath = normalizePath(relative(repositoryRoot, output))
  if (result.passed) console.log(`Frontend full coverage gate passed. Details: ${outputPath}`)
  else console.error(`Frontend full coverage gate failed. Full details: ${outputPath}`)
}

function printOmittedCount(total) {
  if (total > MAX_FAILURES_TO_PRINT) console.error(`  ... ${total - MAX_FAILURES_TO_PRINT} more; see the JSON summary.`)
}

function formatPercentage(value) {
  return `${value.toFixed(2)}%`
}

function capitalize(value) {
  return value[0].toUpperCase() + value.slice(1)
}

function normalizePath(value) {
  return value.split(sep).join('/').replaceAll('//', '/')
}
