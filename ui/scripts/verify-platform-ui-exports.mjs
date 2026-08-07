#!/usr/bin/env node

import { readFile, writeFile } from 'node:fs/promises'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import ts from 'typescript'

const uiRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const packageRoot = resolve(uiRoot, 'ngb-ui-framework')
const manifestPath = resolve(packageRoot, 'package.json')
const entrypointPath = resolve(packageRoot, 'src', 'index.ts')
const snapshotPath = resolve(packageRoot, 'public-api.exports.json')
const update = process.argv.includes('--update')

const manifest = JSON.parse(await readFile(manifestPath, 'utf8'))
const majorVersion = parseMajorVersion(manifest.version)
const exports = await readPublicExports(entrypointPath)

if (update) {
  const snapshot = {
    schemaVersion: 1,
    package: '@ngbplatform/ui',
    majorVersion,
    exports,
  }
  await writeFile(snapshotPath, `${JSON.stringify(snapshot, null, 2)}\n`, 'utf8')
  console.log(`Updated @ngbplatform/ui ${majorVersion}.x public-export snapshot (${exports.length} exports).`)
  process.exit(0)
}

const snapshot = JSON.parse(await readFile(snapshotPath, 'utf8'))
if (snapshot.schemaVersion !== 1 || snapshot.package !== '@ngbplatform/ui') {
  throw new Error(`Unsupported public API snapshot format in ${snapshotPath}.`)
}
if (snapshot.majorVersion !== majorVersion) {
  throw new Error(
    `@ngbplatform/ui major version is ${majorVersion}, but its public API snapshot is for `
      + `${snapshot.majorVersion}.x. Review the intentional major-version changes and run `
      + '`npm run update:api-compat`.',
  )
}

const expected = new Set(snapshot.exports)
const actual = new Set(exports)
const removed = snapshot.exports.filter((name) => !actual.has(name))
const added = exports.filter((name) => !expected.has(name))

if (removed.length > 0 || added.length > 0) {
  const details = [
    removed.length > 0 ? `Removed exports:\n  - ${removed.join('\n  - ')}` : '',
    added.length > 0 ? `Unreviewed new exports:\n  - ${added.join('\n  - ')}` : '',
  ].filter(Boolean).join('\n')
  throw new Error(
    `@ngbplatform/ui public exports differ from the reviewed ${majorVersion}.x snapshot.\n`
      + `${details}\nRun \`npm run update:api-compat\` only after SemVer review.`,
  )
}

console.log(`Verified @ngbplatform/ui ${majorVersion}.x public exports (${exports.length} exports).`)

async function readPublicExports(path) {
  const sourceText = await readFile(path, 'utf8')
  const source = ts.createSourceFile(path, sourceText, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  const names = new Set()

  for (const statement of source.statements) {
    if (ts.isExportDeclaration(statement)) {
      if (!statement.exportClause) {
        throw new Error(
          `Wildcard exports are not allowed in ${path}; list exports explicitly so compatibility can be reviewed.`,
        )
      }
      if (ts.isNamespaceExport(statement.exportClause)) {
        names.add(statement.exportClause.name.text)
        continue
      }
      for (const element of statement.exportClause.elements) {
        names.add(element.name.text)
      }
      continue
    }

    if (!hasExportModifier(statement)) continue

    if (
      ts.isClassDeclaration(statement)
      || ts.isFunctionDeclaration(statement)
      || ts.isInterfaceDeclaration(statement)
      || ts.isTypeAliasDeclaration(statement)
      || ts.isEnumDeclaration(statement)
    ) {
      if (statement.name) names.add(statement.name.text)
      continue
    }

    if (ts.isVariableStatement(statement)) {
      for (const declaration of statement.declarationList.declarations) {
        collectBindingNames(declaration.name, names)
      }
    }
  }

  return [...names].sort((left, right) => left.localeCompare(right))
}

function hasExportModifier(node) {
  return node.modifiers?.some((modifier) => modifier.kind === ts.SyntaxKind.ExportKeyword) === true
}

function collectBindingNames(name, target) {
  if (ts.isIdentifier(name)) {
    target.add(name.text)
    return
  }
  for (const element of name.elements) {
    if (!ts.isOmittedExpression(element)) collectBindingNames(element.name, target)
  }
}

function parseMajorVersion(version) {
  const match = /^(\d+)\.\d+\.\d+(?:[-+].*)?$/.exec(String(version))
  if (!match) throw new Error(`Expected a semantic package version, received "${version}".`)
  return Number(match[1])
}
