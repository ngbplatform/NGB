import { gzipSync } from 'node:zlib'
import { digest } from '../../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'

export function entry(name, value = '', options = {}) {
  const content = Buffer.from(value)
  const header = Buffer.alloc(512)
  header.write(name, 0, 100)
  header.write((options.size ?? content.length).toString(8).padStart(11, '0'), 124, 11)
  header[156] = options.kind ?? 48
  header.write(options.prefix ?? '', 345, 155)
  header.fill(32, 148, 156)
  header.write(header.reduce((sum, byte) => sum + byte, 0).toString(8).padStart(6, '0'), 148, 6)
  header[154] = 0
  return Buffer.concat([header, content, Buffer.alloc((512 - content.length % 512) % 512)])
}

export const archive = entries => gzipSync(Buffer.concat([...entries, Buffer.alloc(1024)]))

function payload() {
  const content = 'verified instructions\n'
  return { schemaVersion: 1, version: '3.4.0', source: { package: 'NGB.Platform.Templates', version: '3.4.0' },
    files: [{ path: 'ai/vendor/instructions.md', role: 'file', content, sha256: digest(content) }] }
}

export function cliFiles() {
  return {
    'package/package.json': JSON.stringify({ name: '@ngbplatform/cli', version: '3.4.0', type: 'module', bin: { ngb: 'infrastructure/ngb.mjs' } }),
    'package/LICENSE': 'license',
    'package/infrastructure/ngb.mjs': 'entry',
    'package/infrastructure/configuration.mjs': 'configuration',
    'package/infrastructure/ngb/cli.mjs': 'cli',
    'package/infrastructure/ngb/contracts.mjs': "export const toolVersion = '3.4.0'\n",
    'package/infrastructure/ngb/kit-payload.json': JSON.stringify(payload()),
    'package/infrastructure/validation/Ngb.Validation.csproj': '<Project/>',
    'package/infrastructure/validation/AssemblyInspector.cs': 'class Inspector {}',
    'package/infrastructure/validation/Program.cs': 'class Program {}',
  }
}

