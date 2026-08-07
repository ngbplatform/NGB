#!/usr/bin/env node

import { readFile } from 'node:fs/promises'

const options = parseArguments(process.argv.slice(2))
const xml = await readFile(options.report, 'utf8')
const coverage = parseRootCoverage(xml)
const failures = []

if (coverage.lineRate * 100 + Number.EPSILON < options.lines) {
  failures.push(`line coverage ${formatRate(coverage.lineRate)} is below ${options.lines.toFixed(2)}%`)
}
if (coverage.branchRate * 100 + Number.EPSILON < options.branches) {
  failures.push(`branch coverage ${formatRate(coverage.branchRate)} is below ${options.branches.toFixed(2)}%`)
}

if (failures.length > 0) {
  console.error(`Backend feature coverage gate failed: ${failures.join('; ')}.`)
  process.exit(1)
}

console.log(
  `Backend feature coverage gate passed: lines ${formatRate(coverage.lineRate)}, branches ${formatRate(coverage.branchRate)}.`,
)

function parseArguments(args) {
  const report = readOption(args, '--report')
  const lines = Number(readOption(args, '--lines'))
  const branches = Number(readOption(args, '--branches'))
  if (!Number.isFinite(lines) || !Number.isFinite(branches)) {
    throw new Error('Coverage thresholds must be finite numbers.')
  }
  return { report, lines, branches }
}

function parseRootCoverage(xml) {
  const match = xml.match(/<coverage\b([^>]*)>/)
  if (!match) throw new Error('The supplied report is not a Cobertura document.')
  const attributes = parseAttributes(match[1] ?? '')
  return {
    lineRate: parseRate(attributes['line-rate'], 'line-rate'),
    branchRate: parseRate(attributes['branch-rate'], 'branch-rate'),
  }
}

function parseRate(value, name) {
  const rate = Number(value)
  if (!Number.isFinite(rate) || rate < 0 || rate > 1) {
    throw new Error(`Cobertura ${name} is invalid: ${JSON.stringify(value)}.`)
  }
  return rate
}

function parseAttributes(value) {
  return Object.fromEntries(
    Array.from(value.matchAll(/([\w-]+)="([^"]*)"/g), (match) => [match[1], match[2]]),
  )
}

function readOption(args, name) {
  const index = args.indexOf(name)
  const value = index >= 0 ? args[index + 1]?.trim() : ''
  if (!value) throw new Error(`Missing required option ${name}.`)
  return value
}

function formatRate(value) {
  return `${(value * 100).toFixed(2)}%`
}
