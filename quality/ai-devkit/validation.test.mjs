import assert from 'node:assert/strict'
import { cp, mkdir, mkdtemp, readFile, realpath, rm, symlink, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'
import { fileURLToPath } from 'node:url'
import { validateApplication } from '../../packaging/templates/content/infrastructure/ngb/validation.mjs'
import { applicationFiles } from '../../packaging/templates/content/infrastructure/ngb/application.mjs'

async function fixture(t) {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'ngb-inspection-')))
  t.after(() => rm(root, { recursive: true, force: true }))
  await cp(fileURLToPath(new URL('../../packaging/templates/content', import.meta.url)), root, {
    recursive: true,
    filter: path => !['.ngb', 'node_modules', 'bin', 'obj'].includes(path.split('/').at(-1)),
  })
  // These are declarative test fixtures, never restored or certified packages.
  const files = await applicationFiles(root)
  for (const [path, bytes] of Object.entries(files)) {
    if (path.endsWith('packages.lock.json') || path === 'web/package-lock.json'
      || path === 'web/package.json' || path === 'Directory.Build.props') {
      await writeFile(join(root, path), bytes.toString('utf8').replaceAll('3.3.0', '3.4.0'))
    }
  }
  return root
}

async function replace(root, path, before, after) {
  await writeFile(join(root, path), (await readFile(join(root, path), 'utf8')).replace(before, after))
}

test('static inspection verifies the declared starter without executing scripts or changing any file', async t => {
  const root = await fixture(t)
  const manifest = JSON.parse(await readFile(join(root, 'web/package.json'), 'utf8'))
  manifest.scripts.build = 'node -e "throw new Error(\'Must not execute during inspection\')"'
  await writeFile(join(root, 'web/package.json'), JSON.stringify(manifest))
  const before = await applicationFiles(root)
  const result = await validateApplication(root)
  assert.equal(result.status, 'passed', JSON.stringify(result.checks))
  assert.deepEqual(await applicationFiles(root), before)
  assert.ok(result.kitSha256 && result.dependencySha256 && result.sourcesSha256)
})

test('inspection rejects hidden projects, symlinks, unapproved package overrides and mixed versions', async t => {
  for (const [id, change] of [
    ['projects.discovery', async root => {
      await mkdir(join(root, 'Hidden'))
      await writeFile(join(root, 'Hidden/Hidden.csproj'), '<Project/>')
    }],
    ['projects.discovery', root => symlink(join(root, 'Shared'), join(root, 'alias'))],
    ['packages.sources', root => writeFile(join(root, 'web/.npmrc'), '//registry.example.test/:_authToken=never-print-this\n')],
    ['packages.sources', root => replace(root, 'NuGet.Config', 'https://api.nuget.org/v3/index.json', 'https://unapproved.test/index.json')],
    ['versions.sdk', root => replace(root, 'Directory.Build.props', '3.4.0', '3.3.0')],
    ['packages.locked', root => replace(root, 'web/package.json', '"@ngbplatform/ui": "3.4.0"', '"@ngbplatform/ui": "3.3.0"')],
    ['kit.integrity', root => writeFile(join(root, 'ai/vendor/instructions.md'), 'unreviewed change\n')],
    ['tests.inventory', root => rm(join(root, 'web/playwright.config.ts'))],
  ]) {
    const root = await fixture(t)
    await change(root)
    const result = await validateApplication(root)
    assert.equal(result.status, 'failed', id)
    assert.equal(result.checks.find(check => check.id === id)?.state, 'failed', id)
    assert.ok(!JSON.stringify(result).includes('never-print-this'))
  }
})

test('conditional build inputs remain explicitly unresolved during inspection', async t => {
  const root = await fixture(t)
  await replace(root, 'NgbApplication.Api/NgbApplication.Api.csproj', '</Project>',
    '<Target Name="Mutate" BeforeTargets="Build"><Exec Command="must-not-execute" /></Target></Project>')
  const result = await validateApplication(root)
  assert.equal(result.checks.find(check => check.id === 'architecture.declarations').state, 'not-run')
  assert.equal(result.status, 'failed')
  const nested = join(root, 'NgbApplication.Api/Directory.Build.props')
  await writeFile(nested, '<Project><Import Project="custom.props"/></Project>')
  assert.equal((await validateApplication(root)).status, 'failed')
})

test('configuration and XML failures remain data-only diagnostics', async t => {
  const root = await fixture(t)
  await writeFile(join(root, 'ngb.config.json'), '{"schemaVersion":999}')
  assert.equal((await validateApplication(root)).checks[0].state, 'failed')
  await cp(fileURLToPath(new URL('../../packaging/templates/content/ngb.config.json', import.meta.url)), join(root, 'ngb.config.json'))
  await writeFile(join(root, 'NgbApplication.Api/NgbApplication.Api.csproj'), '<!DOCTYPE X SYSTEM "file:///private/data"><Project/>')
  const result = await validateApplication(root)
  assert.equal(result.checks.find(check => check.id === 'architecture.declarations').state, 'failed')
  assert.ok(!JSON.stringify(result).includes('file:///private/data'))
})
