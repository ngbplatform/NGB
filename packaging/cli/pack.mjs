import assert from 'node:assert/strict'
import { cp, mkdir, mkdtemp, readFile, realpath, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { createHash } from 'node:crypto'
import { toolVersion } from '../templates/content/infrastructure/ngb/contracts.mjs'
import { executionEnvironment, runTool } from '../templates/content/infrastructure/ngb/process.mjs'
import { prepareTemplate } from '../../ai-devkit/prepare.mjs'
import { verifyCliArchive } from '../templates/content/infrastructure/ngb/archive.mjs'
import { assetPaths } from '../templates/content/infrastructure/ngb/kit-files.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const template = join(repository, 'packaging/templates/content')
const output = join(repository, 'artifacts/npm')

await prepareTemplate()
const kit = JSON.parse(await readFile(join(repository, 'ai-devkit/kit-source.json'), 'utf8'))
assert.equal(kit.version, toolVersion, 'CLI and Kit versions must agree before packaging.')
const stage = await realpath(await mkdtemp(join(tmpdir(), 'ngb-cli-pack-')))
try {
  const payload = join(stage, 'package')
  await mkdir(join(payload, 'infrastructure'), { recursive: true })
  for (const path of ['ngb.mjs', 'configuration.mjs', 'ngb', 'validation']) {
    await cp(join(template, 'infrastructure', path), join(payload, 'infrastructure', path), {
      recursive: true,
      filter: path => !/(?:^|\/)(?:bin|obj|node_modules|\.ngb)(?:\/|$)/.test(path),
    })
  }
  await cp(join(repository, 'LICENSE'), join(payload, 'LICENSE'))
  await writeFile(join(payload, 'package.json'), `${JSON.stringify({
    name: '@ngbplatform/cli',
    version: toolVersion,
    description: 'NGB application CLI and version-matched AI Development Kit tooling.',
    type: 'module',
    license: 'Apache-2.0',
    engines: { node: '>=24.19.0 <25' },
    bin: { ngb: 'infrastructure/ngb.mjs' },
    files: ['infrastructure', 'LICENSE'],
    repository: { type: 'git', url: 'https://github.com/ngbplatform/NGB.git' },
    publishConfig: { access: 'public', registry: 'https://registry.npmjs.org' },
  }, null, 2)}\n`)
  await mkdir(output, { recursive: true })
  const packed = await runTool('npm', ['pack', '--json', '--ignore-scripts', '--pack-destination', output], {
    cwd: payload,
    env: executionEnvironment(join(stage, 'cache')),
  })
  assert.equal(packed.state, 'passed', `CLI packaging failed: ${packed.output}`)
  const [receipt] = JSON.parse(packed.output)
  assert.equal(receipt.name, '@ngbplatform/cli')
  assert.equal(receipt.version, toolVersion)
  const archive = await readFile(join(output, receipt.filename))
  assert.equal(receipt.integrity, `sha512-${createHash('sha512').update(archive).digest('base64')}`)
  const verified = verifyCliArchive(archive, toolVersion)
  const expectedPaths = ['package/LICENSE', 'package/package.json',
    ...await assetPaths(payload, 'infrastructure').then(paths => paths.map(path => `package/${path}`))]
  assert.deepEqual([...verified.files.keys()].sort(), expectedPaths.sort(), 'CLI packaging omitted or introduced payload files.')
  for (const [path, bytes] of verified.files) {
    if (!path.startsWith('package/infrastructure/')) continue
    const relative = path.slice('package/'.length)
    assert.deepEqual(bytes, await readFile(join(template, relative)), `CLI/template payload differs: ${relative}`)
  }
  console.log(JSON.stringify({ filename: receipt.filename, version: toolVersion, integrity: receipt.integrity,
    sha256: createHash('sha256').update(archive).digest('hex'), files: receipt.files.length }))
} finally {
  await rm(stage, { recursive: true, force: true })
}
