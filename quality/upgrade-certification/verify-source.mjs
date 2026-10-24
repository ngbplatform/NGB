import { readFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { verifyFrozenFixture } from '../external-consumer/contracts.mjs'

for (const version of ['3.1.0', '3.2.0']) {
  const fixture = fileURLToPath(new URL(`./fixtures/${version}`, import.meta.url))
  const manifest = JSON.parse(await readFile(new URL(`./source-${version}.json`, import.meta.url), 'utf8'))
  await verifyFrozenFixture(fixture, manifest)
  console.log(`Verified ${Object.keys(manifest.files).length} frozen ${version} files.`)
}
