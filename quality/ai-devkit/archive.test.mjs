import assert from 'node:assert/strict'
import { gzipSync } from 'node:zlib'
import test from 'node:test'
import { readNpmArchive, verifyCliArchive } from '../../packaging/templates/content/infrastructure/ngb/archive.mjs'
import { archive, cliFiles, entry } from './fixtures/cli.mjs'

test('npm tar decoding preserves bytes with bounded regular files and portable names', () => {
  const files = readNpmArchive(archive([
    entry('package/', '', { kind: 53 }), entry('package/a.mjs', 'hello'),
    entry('b'.repeat(100), 'long name', { prefix: 'package' }), entry('package/empty', '', { kind: 0 }),
  ]))
  assert.equal(files.get('package/a.mjs').toString(), 'hello')
  assert.equal(files.get(`package/${'b'.repeat(100)}`).toString(), 'long name')
  assert.equal(files.get('package/empty').length, 0)
})

test('unsafe, duplicate, truncated, extended and oversized archive entries fail closed', () => {
  for (const entries of [
    [], [entry('../escape')], [entry('outside/file')], [entry('package/a', '', { kind: 50 })],
    [entry('package/a'), entry('package/a')], [entry('package/a'), entry('package/A')],
    [entry('package/a'), entry('package/a/child')],
    [entry('package/a/', '', { kind: 53 }), entry('package/a')],
    [entry('package/a/', 'data', { kind: 53 })],
    [entry('package/a', '', { size: 20 * 1024 * 1024 + 1 })],
    [entry('package/a', '', { size: 4096 })],
    Array.from({ length: 4097 }, (_, index) => entry(`package/${index}`)),
  ]) assert.throws(() => readNpmArchive(archive(entries)))
  assert.throws(() => readNpmArchive(Buffer.alloc(16 * 1024 * 1024 + 1)))
  assert.throws(() => readNpmArchive(archive([entry('package/a')]), 0))
  assert.throws(() => readNpmArchive(archive([entry('package/a')]), 1024))
  assert.throws(() => readNpmArchive(gzipSync(Buffer.alloc(513))), /Truncated/)
  assert.throws(() => readNpmArchive(gzipSync(entry('package/a'))), /terminator/)
  assert.throws(() => readNpmArchive(gzipSync(Buffer.concat([entry('package/a'), Buffer.alloc(512)]))), /terminator/)
  assert.throws(() => readNpmArchive(gzipSync(Buffer.concat([Buffer.alloc(512), entry('package/a')]))), /terminator/)
  const corrupted = entry('package/a')
  corrupted[0] = 0
  assert.throws(() => readNpmArchive(archive([corrupted])), /checksum/)
  corrupted[148] = 57
  assert.throws(() => readNpmArchive(archive([corrupted])), /numeric/)
})

test('CLI archives bind package, implementation and bundled Kit versions without install hooks', () => {
  const pack = files => archive(Object.entries(files).map(([name, value]) => entry(name, value)))
  const valid = cliFiles()
  assert.equal(verifyCliArchive(pack(valid), '3.4.0').manifest.name, '@ngbplatform/cli')
  for (const change of [
    files => { delete files['package/package.json'] },
    files => { files['package/package.json'] = 'x'.repeat(65537) },
    files => { files['package/package.json'] = files['package/package.json'].replace('3.4.0', '3.3.0') },
    files => { files['package/package.json'] = JSON.stringify({ ...JSON.parse(files['package/package.json']), scripts: { install: 'untrusted' } }) },
    files => { files['package/unexpected'] = 'bad' },
    files => { delete files['package/infrastructure/ngb/cli.mjs'] },
    files => { files['package/infrastructure/ngb/contracts.mjs'] = '' },
    files => { files['package/infrastructure/ngb/contracts.mjs'] = "export const toolVersion = '3.3.0'\n" },
    files => { files['package/infrastructure/ngb/kit-payload.json'] = files['package/infrastructure/ngb/kit-payload.json'].replaceAll('3.4.0', '3.3.0') },
  ]) {
    const changed = structuredClone(valid)
    change(changed)
    assert.throws(() => verifyCliArchive(pack(changed), '3.4.0'))
  }
})
