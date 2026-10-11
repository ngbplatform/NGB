import { createHash } from 'node:crypto'
import { lstat, readFile, readdir, realpath } from 'node:fs/promises'
import { isAbsolute, matchesGlob, relative, resolve, sep } from 'node:path'

export function assertExactVersion(version) {
  if (typeof version !== 'string' || !/^\d+\.\d+\.\d+$/.test(version)) {
    throw new Error(`An exact stable version is required: ${version}`)
  }
}

export function assertOutsideRepository(repository, consumer) {
  const path = relative(resolve(repository), resolve(consumer))
  if (path === '' || (!path.startsWith(`..${sep}`) && path !== '..' && !isAbsolute(path))) {
    throw new Error('The consumer must be outside the repository.')
  }
}

export async function hashDirectory(directory, excludedPaths = []) {
  const result = {}
  async function visit(path) {
    for (const entry of (await readdir(path)).sort()) {
      const filename = resolve(path, entry)
      const stat = await lstat(filename)
      const relativePath = relative(directory, filename).split(sep).join('/')
      const matchPath = stat.isDirectory() ? `${relativePath}/` : relativePath
      if (excludedPaths.some(pattern => matchesGlob(matchPath, pattern))) continue
      if (stat.isSymbolicLink()) {
        throw new Error(`Symbolic links are forbidden in certified inputs: ${filename}`)
      }
      if (stat.isDirectory()) {
        await visit(filename)
      } else {
        result[relativePath] = createHash('sha256')
          .update(await readFile(filename)).digest('hex')
      }
    }
  }
  await visit(resolve(directory))
  return result
}

export async function verifyFrozenFixture(directory, manifest) {
  assertExactVersion(manifest.platformVersion)
  const actual = await hashDirectory(directory)
  const expected = manifest.files
  const paths = new Set([...Object.keys(actual), ...Object.keys(expected)])
  for (const path of paths) {
    if (actual[path] !== expected[path]) {
      throw new Error(`Frozen source fixture changed: ${path}`)
    }
  }
}

export async function assertConsumerIsolation(directory, repository) {
  assertOutsideRepository(await realpath(repository), await realpath(directory))
  const files = await hashDirectory(directory)
  for (const filename of Object.keys(files)) {
    if (!/\.(?:csproj|props|targets|json|[cm]?[jt]s|vue)$/.test(filename)) continue
    const content = await readFile(resolve(directory, filename), 'utf8')
    if (content.includes(resolve(repository)) || /["'`](?:workspace:|link:)/.test(content)) {
      throw new Error(`Repository/workspace dependency in consumer: ${filename}`)
    }
    if (/\.csproj$/.test(filename)) {
      for (const match of content.matchAll(/ProjectReference\s+Include="([^"]+)"/g)) {
        const target = resolve(directory, filename, '..', match[1])
        const path = relative(resolve(directory), target)
        if (path.startsWith('..') || isAbsolute(path)) {
          throw new Error(`Project reference escapes consumer: ${filename}`)
        }
      }
    }
    if (filename.endsWith('package.json')) {
      const manifest = JSON.parse(content)
      if (manifest.workspaces) throw new Error(`Consumer declares workspaces: ${filename}`)
      for (const version of Object.values({ ...manifest.dependencies, ...manifest.devDependencies })) {
        if (/^(?:file:|\.\.?\/|\/)/.test(version)) {
          throw new Error(`Consumer has a local package dependency: ${filename}`)
        }
      }
    }
  }
}

export function assertGateResults(matrix, evidence, artifactManifestSha256) {
  if (!/^[a-f0-9]{64}$/.test(artifactManifestSha256) || evidence.artifactManifestSha256 !== artifactManifestSha256) {
    throw new Error('Evidence does not identify the certified artifact manifest.')
  }
  const gates = matrix.gates.filter(gate => gate.blocks === evidence.stage)
  if (gates.length === 0) throw new Error('The evidence stage has no required gates.')
  for (const gate of gates) {
    const result = evidence.results[gate.id]
    if (!gate.command || !result || result.status !== 'passed' || !/^[a-f0-9]{64}$/.test(result.evidenceSha256)) {
      throw new Error(`Required gate is missing or failed: ${gate.id}`)
    }
  }
}
