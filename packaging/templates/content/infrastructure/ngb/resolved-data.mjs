import assert from 'node:assert/strict'
import { posix } from 'node:path'
import { portablePath } from './kit-contracts.mjs'

function normalized(path) {
  assert.ok(typeof path === 'string' && path.length <= 4096 && !path.includes('\0'), 'Invalid resolved dependency path.')
  return path.replaceAll('\\', '/').replace(/\/$/, '')
}

export function resolvedProject(root, project, assets, version, packageCache) {
  const application = normalized(root)
  const cache = normalized(packageCache)
  assert.ok([3, 4].includes(assets.version), 'Unsupported NuGet assets schema.')
  assert.equal(normalized(assets.project?.restore?.projectPath), `${application}/${project.path}`,
    'Resolved metadata belongs to a different project.')
  assert.equal(normalized(assets.project.restore.packagesPath), cache, 'Build changed the isolated package cache.')
  assert.deepEqual(Object.keys(assets.packageFolders ?? {}).map(normalized), [cache], 'Unexpected fallback package cache.')
  const sources = Object.keys(assets.project.restore.sources ?? {})
  assert.ok(sources.length > 0 && sources.every(source => source === 'https://api.nuget.org/v3/index.json'
    || normalized(source) === `${application}/.ngb/nuget`), 'Resolved packages used an unapproved source.')
  const entries = Object.entries(assets.libraries ?? {})
  assert.ok(entries.length <= 10000, 'Resolved dependency graph exceeds its size limit.')
  const packages = []
  const projects = []
  for (const [identity, item] of entries) {
    const separator = identity.lastIndexOf('/')
    assert.ok(separator > 0 && separator < identity.length - 1, 'Malformed resolved package identity.')
    const name = identity.slice(0, separator)
    const resolvedVersion = identity.slice(separator + 1)
    if (item.type === 'package') {
      assert.match(name, /^[A-Za-z0-9_.-]+$/)
      assert.match(item.sha512, /^[A-Za-z0-9+/]{86}==$/, `Missing package integrity: ${name}`)
      if (/^NGB\.Platform\./i.test(name)) assert.equal(resolvedVersion, version, `Mixed resolved SDK version: ${name}`)
      packages.push({ name, version: resolvedVersion, sha512: item.sha512 })
    } else {
      assert.equal(item.type, 'project', 'Unknown resolved dependency kind.')
      assert.ok(typeof item.msbuildProject === 'string' && !/^(?:[\\/]|[A-Za-z]:)/.test(item.msbuildProject),
        'Resolved project references must remain relative to the application.')
      const path = posix.normalize(posix.join(posix.dirname(project.path), item.msbuildProject.replaceAll('\\', '/')))
      projects.push(portablePath(path))
    }
  }
  const frameworks = Object.values(assets.project.frameworks ?? {})
  assert.ok(frameworks.length > 0, 'Resolved target frameworks are missing.')
  if (frameworks.some(framework => Object.hasOwn(framework.frameworkReferences ?? {}, 'Microsoft.AspNetCore.App'))) {
    packages.push({ name: 'Microsoft.AspNetCore.App' })
  }
  return { path: project.path, projects, packages, unevaluated: [] }
}

export function assemblyMetadata(value, expectedName) {
  assert.equal(value?.name, expectedName, 'Inspected assembly identity differs from the build output.')
  assert.match(value.sha256, /^[a-f0-9]{64}$/, 'Missing assembly content identity.')
  assert.match(value.version, /^\d+\.\d+\.\d+\.\d+$/)
  assert.ok(Array.isArray(value.references) && value.references.length <= 4096, 'Invalid assembly references.')
  assert.ok(value.references.every(name => typeof name === 'string' && /^[A-Za-z0-9_.-]+$/.test(name)),
    'Malformed assembly reference name.')
  assert.ok(Array.isArray(value.publicContracts) && value.publicContracts.length <= 100000, 'Invalid public contract inventory.')
  for (const contract of value.publicContracts) {
    assert.ok(['type', 'method', 'field'].includes(contract.kind), 'Unknown public contract kind.')
    for (const field of ['type', 'name', 'signature']) {
      assert.ok(typeof contract[field] === 'string' && contract[field].length > 0 && contract[field].length <= 16384,
        'Invalid public contract identifier.')
    }
  }
  return { name: value.name, version: value.version, sha256: value.sha256,
    references: value.references, publicContractCount: value.publicContracts.length }
}
