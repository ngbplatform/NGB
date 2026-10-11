import assert from 'node:assert/strict'
import { parseArgs } from 'node:util'

export const toolVersion = '3.2.0'
export const migrationGuide = 'https://docs.ngbplatform.com/architecture/external-app-upgrades'

export function withManagedIgnores(original) {
  const patterns = ['.ngb/', 'NuGet.Local.Config', 'web/.ngb-packages/*.tgz']
  const lines = original.split(/\r?\n/)
  const missing = patterns.filter(pattern => !lines.includes(pattern))
  if (missing.length === 0) return original
  return `${original.trimEnd()}\n\n# Local NGB packages and upgrade recovery files\n${missing.join('\n')}\n`
}

export function exactVersion(value) {
  assert.match(value, /^\d+\.\d+\.\d+$/, 'Use an exact stable version, for example 3.2.0.')
  return value
}

export function argumentsFor(args) {
  const { values, positionals } = parseArgs({
    args,
    allowPositionals: true,
    options: {
      local: { type: 'boolean' },
      packages: { type: 'string' },
      output: { type: 'string' },
      app: { type: 'string' },
      email: { type: 'string' },
      version: { type: 'string' },
      to: { type: 'string' },
      start: { type: 'boolean' },
      apply: { type: 'boolean' },
      'backup-confirmed': { type: 'boolean' },
      help: { type: 'boolean' },
    },
  })
  const command = values.help ? 'help' : (positionals[0] ?? 'help')
  const allowed = {
    create: ['local', 'packages', 'output', 'email', 'version', 'start'],
    upgrade: ['local', 'packages', 'app', 'to', 'apply'],
    start: ['app'],
    deploy: ['app', 'backup-confirmed'],
    help: Object.keys(values),
  }
  assert.ok(Object.hasOwn(allowed, command), `Unknown command: ${command}. Run ngb.mjs --help.`)
  for (const option of Object.keys(values)) {
    assert.ok(allowed[command].includes(option), `--${option} is not supported by ${command}.`)
  }
  if (command !== 'help') {
    assert.equal(positionals.length, command === 'create' ? 2 : 1, `Unexpected arguments for ${command}.`)
  }
  if (command === 'create') {
    assert.match(positionals[1], /^[A-Za-z][A-Za-z0-9]{1,63}$/, 'Use an application name with 2–64 letters/digits, starting with a letter.')
  }
  if (command === 'upgrade') exactVersion(values.to ?? '')
  if (values.version) exactVersion(values.version)
  return { command, name: positionals[1], ...values }
}

export function jsonText(value, original) {
  const indentation = original.match(/\n([\t ]+)"/)?.[1] ?? '  '
  const newline = original.includes('\r\n') ? '\r\n' : '\n'
  return `${JSON.stringify(value, null, indentation).replaceAll('\n', newline)}${newline}`
}

export function planUpgrade(files, target) {
  exactVersion(target)
  const props = files['Directory.Build.props']
  assert.ok(props, 'Directory.Build.props with NgbPlatformVersion is required.')
  const versions = [...props.matchAll(/<NgbPlatformVersion>([^<]+)<\/NgbPlatformVersion>/g)]
  assert.equal(versions.length, 1, 'Use one unconditional NgbPlatformVersion property.')
  const source = exactVersion(versions[0][1])
  assert.ok(source === '3.1.0' && target === '3.2.0', `No automated migration is registered for ${source} → ${target}. See ${migrationGuide}.`)
  assert.ok(!/<PropertyGroup\b[^>]*\bCondition=[^>]*>(?:(?!<\/PropertyGroup>)[\s\S])*<NgbPlatformVersion>/.test(props),
    'Conditional platform versions need manual migration.')

  const changes = { 'Directory.Build.props': props.replace(versions[0][0], `<NgbPlatformVersion>${target}</NgbPlatformVersion>`) }
  let references = 0
  for (const [path, text] of Object.entries(files)) {
    if (!/\.(csproj|props|targets)$/.test(path)) continue
    const input = changes[path] ?? text
    const updated = input.replace(/<!--[\s\S]*?-->|<Package(?:Reference|Version)\b[^>]*>/g, tag => {
      if (tag.startsWith('<!--') || !/\b(?:Include|Update)=["']NGB\.Platform\.[^"']+["']/.test(tag)) return tag
      references += 1
      const version = tag.match(/\bVersion=(["'])([^"']+)\1/)
      if (!version) {
        assert.ok(tag.startsWith('<PackageReference') && files['Directory.Packages.props'],
          `Unsupported NGB version declaration in ${path}; use an explicit Version or central PackageVersion.`)
        return tag
      }
      if (['$(NgbPlatformVersion)', '[$(NgbPlatformVersion)]'].includes(version[2])) return tag
      assert.ok([source, `[${source}]`].includes(version[2]), `Mixed or unsupported NGB version ${version[2]} in ${path}.`)
      const replacement = version[2].startsWith('[') ? `[${target}]` : target
      return tag.replace(version[0], `Version=${version[1]}${replacement}${version[1]}`)
    })
    if (updated !== text) changes[path] = updated
  }
  assert.ok(references > 0, 'No NGB package references were found.')

  const packageText = files['web/package.json']
  assert.ok(packageText, 'web/package.json is required.')
  const manifest = JSON.parse(packageText)
  assert.equal(manifest.dependencies?.['@ngbplatform/ui'], source, 'Backend and frontend must use the same exact source version.')
  assert.ok(!manifest.workspaces, 'An external application must not depend on an npm workspace.')
  manifest.dependencies['@ngbplatform/ui'] = target
  changes['web/package.json'] = jsonText(manifest, packageText)
  return { source, target, changes, guide: migrationGuide }
}

export function localNugetConfig(original) {
  assert.match(original, /<packageSources>/, 'NuGet.Config must declare packageSources.')
  assert.match(original, /<packageSourceMapping>/, 'NuGet.Config must declare packageSourceMapping.')
  assert.ok(!original.includes('key="ngb-local"'), 'ngb-local is reserved for the generated local feed.')
  return original
    .replace(/<package\s+pattern=["']NGB\.Platform\.[^"']*["']\s*\/>/g, '')
    .replace('</packageSources>', '  <add key="ngb-local" value=".ngb/nuget" />\n  </packageSources>')
    .replace('</packageSourceMapping>', '  <packageSource key="ngb-local"><package pattern="NGB.Platform.*" /></packageSource>\n  </packageSourceMapping>')
}

export function registryLockfile(text, version, integrity) {
  const lock = JSON.parse(text)
  assert.ok(lock.lockfileVersion >= 3, 'npm lockfileVersion 3 or later is required.')
  const entry = lock.packages['node_modules/@ngbplatform/ui']
  assert.equal(entry.version, version, 'npm installed an unexpected UI version.')
  lock.packages[''].dependencies['@ngbplatform/ui'] = version
  entry.resolved = `https://registry.npmjs.org/@ngbplatform/ui/-/ui-${version}.tgz`
  entry.integrity = integrity
  return jsonText(lock, text)
}

export function assertDeployment(services) {
  for (const name of ['api', 'jobs', 'web', 'migrator']) {
    assert.ok(services[name]?.build, `Compose service ${name} must have a build configuration. Deploy custom topologies using their migration guide.`)
  }
  for (const name of ['api', 'jobs']) {
    assert.equal(services[name].depends_on?.migrator?.condition, 'service_completed_successfully',
      `${name} must depend on successful migration.`)
  }
}
