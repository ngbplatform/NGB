#!/usr/bin/env node

import { execFileSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { readFile, writeFile } from 'node:fs/promises'
import { dirname, isAbsolute, relative, resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

const MAX_FAILURES_TO_PRINT = 200

if (process.argv[1] && fileURLToPath(import.meta.url) === resolve(process.argv[1])) {
  await main()
}

async function main() {
  const options = parseArguments(process.argv.slice(2))
  const repositoryRoot = resolve(options.repositoryRoot)
  const solution = resolve(repositoryRoot, options.solution)
  const report = resolve(repositoryRoot, options.report)
  const output = resolve(repositoryRoot, options.output)

  const [xml, discovery] = await Promise.all([
    readFile(report, 'utf8'),
    discoverProductionFiles(repositoryRoot, solution),
  ])

  const reportFiles = parseCobertura(xml, repositoryRoot)
  const requiredFiles = []
  const excludedFiles = []

  for (const source of discovery.files) {
    const contents = await readFile(resolve(repositoryRoot, source), 'utf8')
    const exclusion = classifyCoverageExclusion(source, contents)
    if (exclusion) excludedFiles.push({ file: source, reason: exclusion })
    else requiredFiles.push(source)
  }

  const evaluation = evaluateCoverage({
    requiredFiles,
    excludedFiles,
    reportFiles,
    thresholds: options.thresholds,
  })

  const result = {
    generatedAtUtc: new Date().toISOString(),
    solution: normalizePath(relative(repositoryRoot, solution)),
    report: normalizePath(relative(repositoryRoot, report)),
    testProjects: discovery.testProjects,
    productionProjects: discovery.productionProjects,
    thresholds: options.thresholds,
    ...evaluation,
  }

  await writeFile(output, `${JSON.stringify(result, null, 2)}\n`, 'utf8')
  printResult(result, output, repositoryRoot)

  if (!result.passed) process.exitCode = 1
}

function parseArguments(args) {
  const thresholds = {
    lines: readPercentage(args, '--lines'),
    branches: readPercentage(args, '--branches'),
    methods: readPercentage(args, '--methods'),
  }

  return {
    repositoryRoot: readOption(args, '--repository-root'),
    solution: readOption(args, '--solution'),
    report: readOption(args, '--report'),
    output: readOption(args, '--output'),
    thresholds,
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

function discoverProductionFiles(repositoryRoot, solution) {
  const output = execFileSync('dotnet', ['sln', solution, 'list'], {
    cwd: repositoryRoot,
    encoding: 'utf8',
    maxBuffer: 10 * 1024 * 1024,
  })
  const projects = output
    .split(/\r?\n/u)
    .map((line) => normalizePath(line.trim()))
    .filter((line) => line.endsWith('.csproj'))
    .map((project) => normalizePath(relative(repositoryRoot, resolve(dirname(solution), project))))

  if (projects.length === 0) throw new Error(`No C# projects were found in ${solution}.`)

  const testProjects = []
  const productionProjects = []
  for (const project of projects) {
    const projectXml = readFileSync(resolve(repositoryRoot, project), 'utf8')
    if (isTestProject(project, projectXml)) testProjects.push(project)
    else productionProjects.push(project)
  }

  if (testProjects.length === 0) throw new Error('No backend test projects were discovered.')
  if (productionProjects.length === 0) throw new Error('No backend production projects were discovered.')

  const files = new Set()
  for (const project of productionProjects) {
    const itemsJson = execFileSync(
      'dotnet',
      ['msbuild', resolve(repositoryRoot, project), '-nologo', '-property:Configuration=Release', '-getItem:Compile'],
      { cwd: repositoryRoot, encoding: 'utf8', maxBuffer: 50 * 1024 * 1024 },
    )
    const items = JSON.parse(itemsJson).Items?.Compile ?? []
    for (const item of items) {
      const fullPath = item.FullPath || resolve(dirname(resolve(repositoryRoot, project)), item.Identity)
      const repositoryPath = normalizePath(relative(repositoryRoot, fullPath))
      if (!repositoryPath.startsWith('../') && !isGeneratedBuildPath(repositoryPath)) files.add(repositoryPath)
    }
  }

  return {
    files: [...files].sort(),
    productionProjects: productionProjects.sort(),
    testProjects: testProjects.sort(),
  }
}

function isTestProject(project, xml) {
  return /Tests\.csproj$/iu.test(project)
    || /<IsTestProject>\s*true\s*<\/IsTestProject>/iu.test(xml)
    || /Microsoft\.NET\.Test\.Sdk/iu.test(xml)
}

export function classifyCoverageExclusion(file, source) {
  const normalizedFile = normalizePath(file)
  if (isGeneratedBuildPath(normalizedFile)) return 'generated-build-output'
  if (isGeneratedSource(normalizedFile, source)) return 'generated-source'

  const code = maskCSharpNonCode(source)
  if (isAttributeOrUsingDeclarationsOnly(code)) return 'declaration-only-attributes-or-usings'

  const typeKinds = findDeclaredTypeKinds(code)

  if (typeKinds.length > 0 && typeKinds.every((kind) => kind === 'enum' || kind === 'delegate')) {
    return 'declaration-only-enum-or-delegate'
  }
  if (typeKinds.length > 0 && typeKinds.every((kind) => kind === 'interface') && isPureInterface(code)) {
    return 'declaration-only-interface'
  }
  if (isConstOnlyContainer(code, typeKinds)) return 'declaration-only-constants'
  if (isDeclarativeDdlMigration(normalizedFile, code, typeKinds)) return 'declarative-ddl-migration'
  if (isSourceGeneratorDeclarationsOnly(code, typeKinds)) return 'source-generator-declarations'

  return null
}

function findDeclaredTypeKinds(code) {
  const declarations = [...code.matchAll(
    /\b(?:(?:public|internal|private|protected|file|abstract|sealed|static|partial|readonly|ref|unsafe|new)\s+)*(class|struct|record|interface|enum)\s+(?:(?:class|struct)\s+)?[A-Za-z_]\w*|\bdelegate\b/gu,
  )]
  return declarations.map((match) => match[1] ?? 'delegate')
}

function isAttributeOrUsingDeclarationsOnly(code) {
  const remainder = code
    .replace(/^\s*#.*$/gmu, '')
    .replace(/\b(?:global\s+)?using\s+[^;]+;/gu, '')
    .replace(/\[(?:assembly|module)\s*:[^\]]+\]/gu, '')
    .trim()
  return remainder.length === 0
}

function isGeneratedBuildPath(file) {
  return file.split('/').some((part) => part === 'bin' || part === 'obj')
}

function isGeneratedSource(file, source) {
  return /(?:^|\/)[^/]+\.(?:g|generated|designer)\.cs$/iu.test(file)
    || /<auto-generated(?:\s+\/?)?>/iu.test(source.slice(0, 2048))
    || /\bGeneratedCodeAttribute\s*\(/u.test(source)
    || /\[\s*GeneratedCode\s*\(/u.test(source)
}

function isPureInterface(code) {
  if (/=>/u.test(code)) return false
  if (/\b(?:get|set|init|add|remove)\s*\{/u.test(code)) return false
  if (/\)\s*(?:where\s+[^{}]+)?\{/u.test(code)) return false
  return true
}

function isConstOnlyContainer(code, typeKinds) {
  if (typeKinds.length === 0 || typeKinds.some((kind) => !['class', 'struct'].includes(kind))) return false
  if (!/\bconst\b/u.test(code)) return false
  if (/=>|\b(?:new|return|throw|yield|await|if|for|foreach|while|switch|try|catch|lock)\b/u.test(code)) return false
  if (/\b(?:get|set|init|add|remove)\s*\{/u.test(code)) return false
  if (/\b[A-Za-z_]\w*\s*\([^;{}]*\)\s*\{/u.test(code)) return false

  const withoutDirectives = code
    .replace(/^\s*#.*$/gmu, '')
    .replace(/\b(?:global\s+)?using\s+[^;]+;/gu, '')
    .replace(/\bnamespace\s+[\w.]+\s*;/gu, '')
  const statements = withoutDirectives
    .split(';')
    .slice(0, -1)
    .map((statement) => statement.replace(/[{}]/gu, ' ').trim())
    .filter(Boolean)

  return statements.length > 0 && statements.every((statement) => {
    if (/\b(?:class|struct)\b/u.test(statement)) return /\bconst\b/u.test(statement)
    return /\bconst\b/u.test(statement)
  })
}

function isDeclarativeDdlMigration(file, code, typeKinds) {
  if (!file.includes('/Migrations/') || !/:\s*IDdlObject\b/u.test(code)) return false
  if (typeKinds.length !== 1 || typeKinds[0] !== 'class') return false
  if (/\b(?:if|for|foreach|while|switch|throw|try|catch|lock|await|yield|return|new)\b/u.test(code)) return false
  if (/\)\s*\{/u.test(code)) return false
  if (/=>\s*[A-Za-z_]\w*\s*\(/u.test(code)) return false

  const expressionMethods = [...code.matchAll(/\b([A-Za-z_]\w*)\s*\([^;{}]*\)\s*=>/gu)]
    .map((match) => match[1])
  if (expressionMethods.length !== 1 || expressionMethods[0] !== 'Generate') return false

  const expressionProperties = [...code.matchAll(/\b([A-Za-z_]\w*)\s*=>/gu)]
    .map((match) => match[1])
    .filter((name) => name !== 'Generate')
  return expressionProperties.length > 0
    && expressionProperties.every((name) => name === 'Name' || name === 'Sql')
}

function isSourceGeneratorDeclarationsOnly(code, typeKinds) {
  if (typeKinds.length !== 1 || typeKinds[0] !== 'class') return false
  if (!/\[\s*LoggerMessage\s*\(/u.test(code)) return false
  if (/=>|\b(?:if|for|foreach|while|switch|throw|try|catch|lock|await|yield|return|new)\b/u.test(code)) return false
  if (/\)\s*\{/u.test(code)) return false

  const methods = [...code.matchAll(/\b(?:public|internal|private|protected)\s+static\s+partial\s+[^;{}]+\([^;{}]*\)\s*;/gu)]
  return methods.length > 0
}

function maskCSharpNonCode(source) {
  let result = ''
  let index = 0
  while (index < source.length) {
    if (source.startsWith('//', index)) {
      const end = source.indexOf('\n', index + 2)
      const next = end < 0 ? source.length : end
      result += maskPreservingNewlines(source.slice(index, next))
      index = next
      continue
    }
    if (source.startsWith('/*', index)) {
      const end = source.indexOf('*/', index + 2)
      const next = end < 0 ? source.length : end + 2
      result += maskPreservingNewlines(source.slice(index, next))
      index = next
      continue
    }

    const stringStart = findStringStart(source, index)
    if (stringStart) {
      const next = findStringEnd(source, index, stringStart)
      result += maskPreservingNewlines(source.slice(index, next))
      index = next
      continue
    }

    if (source[index] === '\'') {
      const next = findQuotedEnd(source, index + 1, '\'', false)
      result += maskPreservingNewlines(source.slice(index, next))
      index = next
      continue
    }

    result += source[index]
    index += 1
  }
  return result
}

function findStringStart(source, index) {
  const prefixes = ['$@"', '@$"', '$"', '@"', '"']
  for (const prefix of prefixes) {
    if (source.startsWith(prefix, index)) {
      const quoteIndex = index + prefix.length - 1
      let quoteCount = 1
      while (source[quoteIndex + quoteCount] === '"') quoteCount += 1
      return { prefix, quoteCount, verbatim: prefix.includes('@') }
    }
  }
  return null
}

function findStringEnd(source, index, start) {
  const quoteIndex = index + start.prefix.length - 1
  if (start.quoteCount >= 3) {
    const delimiter = '"'.repeat(start.quoteCount)
    const end = source.indexOf(delimiter, quoteIndex + start.quoteCount)
    return end < 0 ? source.length : end + delimiter.length
  }
  return findQuotedEnd(source, quoteIndex + 1, '"', start.verbatim)
}

function findQuotedEnd(source, index, quote, verbatim) {
  let cursor = index
  while (cursor < source.length) {
    if (source[cursor] === quote) {
      if (verbatim && source[cursor + 1] === quote) {
        cursor += 2
        continue
      }
      return cursor + 1
    }
    if (!verbatim && source[cursor] === '\\') cursor += 2
    else cursor += 1
  }
  return source.length
}

function maskPreservingNewlines(value) {
  return value.replace(/[^\r\n]/gu, ' ')
}

export function parseCobertura(xml, repositoryRoot) {
  if (!/<coverage\b/u.test(xml)) throw new Error('The supplied report is not a Cobertura document.')

  const files = new Map()
  for (const classMatch of xml.matchAll(/<class\b([^>]*)>([\s\S]*?)<\/class>/gu)) {
    const attributes = parseAttributes(classMatch[1] ?? '')
    const filename = resolveReportFilename(attributes.filename, repositoryRoot)
    if (!filename) continue

    const body = classMatch[2] ?? ''
    const file = getOrAddFile(files, filename)
    file.reportClasses += 1
    const className = decodeXml(attributes.name ?? '')

    const directLinesStart = body.lastIndexOf('<lines>')
    if (directLinesStart >= 0) {
      const directLinesEnd = body.indexOf('</lines>', directLinesStart)
      const directLines = body.slice(directLinesStart + '<lines>'.length, directLinesEnd)
      for (const lineMatch of directLines.matchAll(/<line\b([^>]*?)(?:\/>|>)/gu)) {
        const lineAttributes = parseAttributes(lineMatch[1] ?? '')
        const number = Number(lineAttributes.number)
        const hits = Number(lineAttributes.hits)
        if (!Number.isInteger(number) || number < 1 || !Number.isFinite(hits)) continue
        file.lines.set(number, Math.max(file.lines.get(number) ?? 0, hits))

        const condition = parseConditionCoverage(lineAttributes['condition-coverage'])
        if (condition) {
          const branchKey = `${className}:${number}`
          const previous = file.branches.get(branchKey) ?? { covered: 0, total: 0 }
          file.branches.set(branchKey, {
            covered: Math.max(previous.covered, condition.covered),
            total: Math.max(previous.total, condition.total),
          })
        }
      }
    }

    for (const methodMatch of body.matchAll(/<method\b([^>]*)>([\s\S]*?)<\/method>/gu)) {
      const methodAttributes = parseAttributes(methodMatch[1] ?? '')
      const methodLines = [...(methodMatch[2] ?? '').matchAll(/<line\b([^>]*?)(?:\/>|>)/gu)]
        .map((match) => parseAttributes(match[1] ?? ''))
      if (methodLines.length === 0) continue
      const key = `${className}.${decodeXml(methodAttributes.name ?? '')}${decodeXml(methodAttributes.signature ?? '')}`
      file.methods.set(key, methodLines.some((line) => Number(line.hits) > 0))
    }
  }

  return files
}

function resolveReportFilename(filename, repositoryRoot) {
  if (!filename) return null
  const decoded = decodeXml(filename).replaceAll('\\', '/')
  const repositoryPath = isAbsolute(decoded)
    ? normalizePath(relative(repositoryRoot, decoded))
    : normalizePath(decoded.replace(/^\.\//u, ''))
  if (repositoryPath.startsWith('../') || isGeneratedBuildPath(repositoryPath)) return null
  return repositoryPath
}

function getOrAddFile(files, filename) {
  if (!files.has(filename)) {
    files.set(filename, {
      lines: new Map(),
      branches: new Map(),
      methods: new Map(),
      reportClasses: 0,
    })
  }
  return files.get(filename)
}

function parseConditionCoverage(value) {
  if (!value) return null
  const match = value.match(/\((\d+)\s*\/\s*(\d+)\)/u)
  if (!match) return null
  return { covered: Number(match[1]), total: Number(match[2]) }
}

function parseAttributes(value) {
  return Object.fromEntries(
    [...value.matchAll(/([\w-]+)="([^"]*)"/gu)].map((match) => [match[1], decodeXml(match[2])]),
  )
}

function decodeXml(value) {
  return value
    .replaceAll('&quot;', '"')
    .replaceAll('&apos;', '\'')
    .replaceAll('&lt;', '<')
    .replaceAll('&gt;', '>')
    .replaceAll('&amp;', '&')
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

    const metrics = calculateMetrics(coverage)
    addMetrics(totals, metrics)
    const failures = []
    for (const metric of ['lines', 'branches', 'methods']) {
      if (metrics[metric].percentage + Number.EPSILON < thresholds[metric]) {
        failures.push(`${metric} ${formatPercentage(metrics[metric].percentage)}`)
      }
    }
    perFile.push({ file: filename, ...metrics, failures })
  }

  const overall = finalizeMetrics(totals)
  const failingFiles = perFile.filter((file) => file.failures.length > 0)
  const overallFailures = []
  for (const metric of ['lines', 'branches', 'methods']) {
    if (overall[metric].percentage + Number.EPSILON < thresholds[metric]) {
      overallFailures.push(`${metric} ${formatPercentage(overall[metric].percentage)}`)
    }
  }

  const exclusionSummary = Object.entries(
    Object.groupBy(excludedFiles, (entry) => entry.reason),
  ).map(([reason, files]) => ({ reason, count: files.length }))
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
  return {
    lines: { covered: 0, total: 0 },
    branches: { covered: 0, total: 0 },
    methods: { covered: 0, total: 0 },
  }
}

function calculateMetrics(file) {
  const metrics = emptyMetrics()
  metrics.lines.total = file.lines.size
  metrics.lines.covered = [...file.lines.values()].filter((hits) => hits > 0).length
  for (const branch of file.branches.values()) {
    metrics.branches.covered += branch.covered
    metrics.branches.total += branch.total
  }
  metrics.methods.total = file.methods.size
  metrics.methods.covered = [...file.methods.values()].filter(Boolean).length
  return finalizeMetrics(metrics)
}

function addMetrics(target, source) {
  for (const metric of ['lines', 'branches', 'methods']) {
    target[metric].covered += source[metric].covered
    target[metric].total += source[metric].total
  }
}

function finalizeMetrics(metrics) {
  return Object.fromEntries(Object.entries(metrics).map(([name, value]) => [name, {
    ...value,
    percentage: value.total === 0 ? 100 : value.covered / value.total * 100,
  }]))
}

function printResult(result, output, repositoryRoot) {
  console.log('Backend full coverage gate')
  console.log(`  Test projects:       ${result.testProjects.length}`)
  console.log(`  Production projects: ${result.productionProjects.length}`)
  console.log(`  Source files:        ${result.sourceFiles.discovered}`)
  console.log(`  Required files:      ${result.sourceFiles.required}`)
  console.log(`  Excluded files:      ${result.sourceFiles.excluded}`)
  console.log(`  Complete files:      ${result.sourceFiles.presentInReport}/${result.sourceFiles.required}`)
  for (const metric of ['lines', 'branches', 'methods']) {
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
  if (result.passed) console.log(`Backend full coverage gate passed. Details: ${outputPath}`)
  else console.error(`Backend full coverage gate failed. Full details: ${outputPath}`)
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
