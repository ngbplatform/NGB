import { readFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { verifyFrozenFixture } from '../external-consumer/contracts.mjs'

const fixture = fileURLToPath(new URL('./fixtures/3.1.0', import.meta.url))
const manifest = JSON.parse(await readFile(new URL('./source-3.1.0.json', import.meta.url), 'utf8'))
await verifyFrozenFixture(fixture, manifest)
console.log(`Verified ${Object.keys(manifest.files).length} frozen 3.1.0 files.`)
