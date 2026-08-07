import fs from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

import libCoverage from 'istanbul-lib-coverage'
import libReport from 'istanbul-lib-report'
import reports from 'istanbul-reports'

const workspaceRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const artifactsRoot = path.resolve(workspaceRoot, '../artifacts/coverage')
const inputDirectories = [
  path.join(artifactsRoot, 'frontend-diff-framework'),
  path.join(artifactsRoot, 'frontend-diff-apps'),
]
const outputDirectory = path.join(artifactsRoot, 'frontend-diff')

const coverageMap = libCoverage.createCoverageMap({})

for (const inputDirectory of inputDirectories) {
  const reportPath = path.join(inputDirectory, 'coverage-final.json')
  const report = JSON.parse(await fs.readFile(reportPath, 'utf8'))
  coverageMap.merge(report)
}

await fs.rm(outputDirectory, { recursive: true, force: true })
await fs.mkdir(outputDirectory, { recursive: true })

const context = libReport.createContext({
  coverageMap,
  dir: outputDirectory,
})

for (const [name, options] of [
  ['text', {}],
  ['html', {}],
  ['json', { file: 'coverage-final.json' }],
  ['json-summary', { file: 'coverage-summary.json' }],
  ['lcovonly', { file: 'lcov.info' }],
]) {
  reports.create(name, options).execute(context)
}
