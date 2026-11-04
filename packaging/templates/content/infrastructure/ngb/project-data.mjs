import assert from 'node:assert/strict'
import { posix } from 'node:path'
import { distinctPaths, objectKeys, portablePath } from './kit-contracts.mjs'

const roles = ['definitions', 'runtime', 'persistence', 'api', 'worker', 'migrator', 'tests', 'tooling']

export function validateConfiguration(value) {
  objectKeys(value, ['schemaVersion', 'solution', 'projects', 'frontend', 'tests'])
  assert.equal(value.schemaVersion, 1, 'Unsupported ngb.config.json schema.')
  assert.match(portablePath(value.solution), /\.slnx?$/)
  assert.ok(Array.isArray(value.projects) && value.projects.length > 0, 'Declare application projects and their roles.')
  distinctPaths(value.projects.map(project => project.path))
  for (const project of value.projects) {
    objectKeys(project, ['path', 'role'])
    assert.match(project.path, /\.csproj$/)
    assert.ok(roles.includes(project.role), `Unknown project role: ${project.role}`)
  }
  objectKeys(value.frontend, ['path'])
  portablePath(value.frontend.path)
  objectKeys(value.tests, ['unit', 'integration', 'e2e'])
  const declared = new Map(value.projects.map(project => [project.path, project.role]))
  for (const profile of ['unit', 'integration']) {
    assert.ok(Array.isArray(value.tests[profile]) && value.tests[profile].length > 0, `Missing ${profile} test projects.`)
    distinctPaths(value.tests[profile])
    for (const path of value.tests[profile]) assert.equal(declared.get(path), 'tests', `Undeclared test project: ${path}`)
  }
  assert.ok(!value.tests.unit.some(path => value.tests.integration.includes(path)), 'Unit and integration suites must be distinct.')
  objectKeys(value.tests.e2e, ['config'])
  portablePath(value.tests.e2e.config)
  return value
}

function decodeXml(value) {
  const entities = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'" }
  return value.replace(/&(?:([A-Za-z]+|#[0-9]+|#x[0-9A-Fa-f]+);)?/g, (_, entity) => {
    assert.ok(entity, 'Malformed XML character or entity reference.')
    if (Object.hasOwn(entities, entity)) return entities[entity]
    assert.ok(entity.startsWith('#'), 'Unsupported named XML entity.')

    const code = entity.startsWith('#x') ? Number.parseInt(entity.slice(2), 16) : Number(entity.slice(1))
    // XML 1.0 Char permits tab, LF, CR and these Unicode ranges.
    assert.ok([9, 10, 13].includes(code) || (code >= 0x20 && code <= 0xd7ff)
      || (code >= 0xe000 && code <= 0xfffd) || (code >= 0x10000 && code <= 0x10ffff),
    'Invalid XML character reference.')
    return String.fromCodePoint(code)
  })
}

export function readProjectXml(source) {
  return readXml(source, 1024 * 1024, 10000, 'Project XML')
}

export function readTestXml(source) {
  return readXml(source, 8 * 1024 * 1024, 100000, 'Test report XML')
}

function readXml(source, maxBytes, maxTokens, description) {
  assert.ok(typeof source === 'string' && Buffer.byteLength(source, 'utf8') <= maxBytes, `${description} exceeds its size limit.`)
  const text = source.replace(/^\uFEFF/, '').replace(/^\s*<\?xml\s[^?]*\?>/, '').replace(/<!--[\s\S]*?-->/g, '')
  assert.ok(!/<!|<\?/.test(text), 'XML directives and DTDs are not supported by inspection.')
  const root = { name: '#document', attributes: {}, children: [], text: '' }
  const stack = [root]
  const tokens = text.match(/<[^>]*>|[^<]+/g) ?? []
  assert.equal(tokens.join(''), text, 'Malformed project XML.')
  assert.ok(tokens.length <= maxTokens, `${description} exceeds its token limit.`)
  for (const token of tokens) {
    const parent = stack.at(-1)
    if (!token.startsWith('<')) {
      parent.text += decodeXml(token)
      continue
    }
    const end = token.match(/^<\/([A-Za-z_][A-Za-z0-9_.-]*)\s*>$/)
    if (end) {
      assert.ok(stack.length > 1 && parent.name === end[1], 'Mismatched XML closing tag.')
      stack.pop()
      continue
    }
    const start = token.match(/^<([A-Za-z_][A-Za-z0-9_.-]*)(\s[^<>]*?)?\s*(\/?)>$/)
    assert.ok(start, 'Unsupported project XML element.')
    const attributes = {}
    const remaining = (start[2] ?? '').replace(/([A-Za-z_][A-Za-z0-9_.:-]*)\s*=\s*("[^"]*"|'[^']*')/g,
      (_, name, quoted) => {
        assert.ok(!Object.hasOwn(attributes, name), 'Duplicate XML attribute.')
        attributes[name] = decodeXml(quoted.slice(1, -1))
        return ''
      })
    assert.equal(remaining.trim(), '', 'Malformed XML attributes.')
    const node = { name: start[1], attributes, children: [], text: '' }
    parent.children.push(node)
    if (!start[3]) stack.push(node)
    assert.ok(stack.length <= 32, 'Project XML exceeds its nesting limit.')
  }
  assert.equal(stack.length, 1, 'Unclosed project XML element.')
  assert.equal(root.children.length, 1, 'Exactly one XML document root is required.')
  assert.equal(root.text.trim(), '', 'Unexpected data outside XML root.')
  return root.children[0]
}

export function projectReferences(path, source) {
  const root = readProjectXml(source)
  assert.equal(root.name, 'Project', 'Expected an MSBuild Project document.')
  const packages = []
  const projects = []
  const unevaluated = new Set()
  function visit(node) {
    if (['Import', 'Choose', 'UsingTask', 'Target', 'ItemDefinitionGroup'].includes(node.name) || Object.hasOwn(node.attributes, 'Condition')) {
      unevaluated.add(node.name)
    }
    if (node.name === 'PackageReference') {
      const name = node.attributes.Include ?? node.attributes.Update
      if (!name || /[$@%*;]/.test(name)) unevaluated.add('PackageReference')
      else packages.push({ name, version: node.attributes.Version ?? node.children.find(child => child.name === 'Version')?.text.trim() })
    }
    if (node.name === 'ProjectReference') {
      const reference = node.attributes.Include
      if (!reference || /[$@%*;]/.test(reference)) unevaluated.add('ProjectReference')
      else {
        assert.ok(!/^(?:[\\/]|[A-Za-z]:)/.test(reference), 'Project references must be relative to the application.')
        const normalized = posix.normalize(posix.join(posix.dirname(path), reference.replaceAll('\\', '/')))
        portablePath(normalized)
        projects.push(normalized)
      }
    }
    if (node.name === 'FrameworkReference' && node.attributes.Include === 'Microsoft.AspNetCore.App') {
      packages.push({ name: 'Microsoft.AspNetCore.App' })
    }
    for (const child of node.children) visit(child)
  }
  visit(root)
  if (root.attributes.Sdk?.includes('Web')) packages.push({ name: 'Microsoft.AspNetCore.App' })
  return { path, packages, projects, unevaluated: [...unevaluated] }
}

export function platformVersion(source) {
  const root = readProjectXml(source)
  assert.equal(root.name, 'Project', 'Expected platform properties in an MSBuild Project document.')
  const versions = []
  function visit(node, conditional) {
    const dependent = conditional || Object.hasOwn(node.attributes, 'Condition')
      || ['Choose', 'When', 'Otherwise', 'Target'].includes(node.name)
    if (node.name === 'NgbPlatformVersion') {
      assert.ok(!dependent, 'NgbPlatformVersion must be unconditional.')
      versions.push(node.text.trim())
    }
    node.children.forEach(child => visit(child, dependent))
  }
  visit(root, false)
  assert.equal(versions.length, 1, 'Declare exactly one unconditional NgbPlatformVersion.')
  return versions[0]
}

export function layerViolations(configuration, facts) {
  const projects = new Map(configuration.projects.map(project => [project.path, project.role]))
  const allowed = {
    definitions: ['definitions'],
    runtime: ['definitions', 'runtime'],
    persistence: ['definitions', 'persistence'],
  }
  const violations = []
  for (const fact of facts) {
    const role = projects.get(fact.path)
    assert.ok(role, `Project omitted from configuration: ${fact.path}`)
    for (const dependency of fact.projects) {
      const target = projects.get(dependency)
      if (!target || allowed[role] && !allowed[role].includes(target)) {
        violations.push({ path: fact.path, dependency, reason: 'Forbidden or undeclared project dependency.' })
      }
    }
    if (!allowed[role]) continue
    for (const { name } of fact.packages) {
      const provider = /PostgreSql|Dapper|Npgsql|EntityFrameworkCore/i.test(name)
      const host = /AspNetCore|\.Api$|\.Hosting|\.Migrator|\.Watchdog/i.test(name)
      const runtime = /\.Runtime|\.BackgroundJobs/i.test(name)
      if (host || role !== 'persistence' && provider || role !== 'runtime' && runtime) {
        violations.push({ path: fact.path, dependency: name, reason: `Forbidden ${role} package dependency.` })
      }
    }
  }
  return violations
}
