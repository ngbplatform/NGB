import assert from 'node:assert/strict'
import { gunzipSync } from 'node:zlib'
import { distinctPaths, portablePath, validatePayload } from './kit-contracts.mjs'

const maximumArchiveBytes = 64 * 1024 * 1024

function text(header, start, length) {
  const bytes = header.subarray(start, start + length)
  const end = bytes.indexOf(0)
  return bytes.subarray(0, end < 0 ? bytes.length : end).toString('utf8')
}

function octal(header, start, length) {
  const value = text(header, start, length).trim()
  assert.match(value, /^[0-7]+$/, 'Invalid tar numeric field.')
  const number = Number.parseInt(value, 8)
  assert.ok(Number.isSafeInteger(number), 'Tar numeric field exceeds its limit.')
  return number
}

export function readNpmArchive(compressed, limit = maximumArchiveBytes) {
  assert.ok(Buffer.isBuffer(compressed) && compressed.length <= 16 * 1024 * 1024, 'Invalid compressed CLI archive size.')
  assert.ok(Number.isSafeInteger(limit) && limit > 0 && limit <= maximumArchiveBytes, 'Invalid archive expansion limit.')
  const bytes = gunzipSync(compressed, { maxOutputLength: limit })
  assert.equal(bytes.length % 512, 0, 'Truncated tar archive.')
  const files = new Map()
  const directories = []
  let offset = 0
  let terminated = false
  while (offset < bytes.length) {
    const header = bytes.subarray(offset, offset + 512)
    if (header.every(byte => byte === 0)) {
      assert.ok(bytes.length - offset >= 1024 && bytes.subarray(offset).every(byte => byte === 0), 'Invalid tar terminator.')
      terminated = true
      break
    }
    const checksum = header.reduce((sum, byte, index) => sum + (index >= 148 && index < 156 ? 32 : byte), 0)
    assert.equal(octal(header, 148, 8), checksum, 'Tar header checksum mismatch.')
    const prefix = text(header, 345, 155)
    const name = `${prefix ? `${prefix}/` : ''}${text(header, 0, 100)}`
    const kind = header[156]
    const directory = kind === 53
    const path = portablePath(directory ? name.replace(/\/$/, '') : name)
    assert.ok(path === 'package' || path.startsWith('package/'), 'CLI archive must use one package root.')
    assert.ok([0, 48, 53].includes(kind), 'CLI archives cannot contain links, extended headers or special files.')
    assert.ok(!files.has(path) && !directories.includes(path), 'Duplicate tar entry.')
    const size = octal(header, 124, 12)
    assert.ok(size <= 20 * 1024 * 1024 && offset + 512 + size <= bytes.length, 'Tar entry exceeds its bounds.')
    if (directory) {
      assert.equal(size, 0, 'Tar directory contains file data.')
      directories.push(path)
    } else {
      files.set(path, Buffer.from(bytes.subarray(offset + 512, offset + 512 + size)))
    }
    assert.ok(files.size + directories.length <= 4096, 'CLI archive contains too many entries.')
    offset += 512 + Math.ceil(size / 512) * 512
  }
  assert.ok(terminated && files.size > 0, 'CLI archive is empty or lacks its terminator.')
  distinctPaths([...files.keys()])
  for (const path of directories) assert.ok(!files.has(path), 'File/directory collision in archive.')
  return files
}

export function verifyCliArchive(compressed, version) {
  const files = readNpmArchive(compressed)
  const metadata = files.get('package/package.json')
  assert.ok(metadata && metadata.length <= 65536, 'Missing or oversized CLI package metadata.')
  const manifest = JSON.parse(metadata.toString('utf8'))
  assert.equal(manifest.name, '@ngbplatform/cli')
  assert.equal(manifest.version, version, 'CLI archive version differs from its requested identity.')
  assert.equal(manifest.type, 'module')
  assert.deepEqual(manifest.bin, { ngb: 'infrastructure/ngb.mjs' })
  assert.ok(!manifest.scripts && !manifest.dependencies && !manifest.optionalDependencies && !manifest.bundledDependencies
    && !manifest.bundleDependencies && !manifest.peerDependencies && !manifest.devDependencies,
    'The official CLI must not require install hooks or additional runtime packages.')
  for (const path of files.keys()) {
    assert.ok(path === 'package/package.json' || path === 'package/LICENSE'
      || /^package\/infrastructure\/(?:ngb\.mjs|configuration\.mjs|ngb\/[a-z0-9-]+\.(?:mjs|json)|validation\/[A-Za-z0-9.-]+\.(?:cs|csproj|json|runsettings))$/.test(path),
    `Unexpected CLI package entry: ${path}`)
  }
  for (const path of ['ngb.mjs', 'configuration.mjs', 'ngb/cli.mjs', 'ngb/contracts.mjs', 'ngb/kit-payload.json',
    'validation/Ngb.Validation.csproj', 'validation/AssemblyInspector.cs', 'validation/Program.cs']) {
    assert.ok(files.has(`package/infrastructure/${path}`), `CLI archive is incomplete: ${path}`)
  }
  const contracts = files.get('package/infrastructure/ngb/contracts.mjs').toString('utf8')
  const declarations = [...contracts.matchAll(/^export const toolVersion = '([^']+)'$/gm)]
  assert.equal(declarations.length, 1, 'CLI implementation must declare one exact tool version.')
  assert.equal(declarations[0][1], version, 'CLI metadata and implementation versions differ.')
  const payload = validatePayload(JSON.parse(files.get('package/infrastructure/ngb/kit-payload.json').toString('utf8')))
  assert.equal(payload.version, version, 'CLI and bundled Kit versions differ.')
  return { manifest, files }
}
