import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import { toolchain } from './environment.mjs'

test('host tools, CI and Linux quality image use one version contract', async () => {
  const dockerfile = await readFile(new URL('./Dockerfile.quality', import.meta.url), 'utf8')
  const workflow = await readFile(new URL('../../.github/workflows/external-app-certification.yml', import.meta.url), 'utf8')
  assert.ok(dockerfile.includes(`dotnet/sdk:${toolchain.dotnet}`))
  assert.ok(dockerfile.includes(`node:${toolchain.node}`))
  assert.ok(dockerfile.includes(`grafana/k6:${toolchain.k6}@sha256:`))
  assert.ok(dockerfile.includes(`playwright:v${toolchain.playwright}-noble`))
  assert.ok(workflow.includes(`dotnet-version: ${toolchain.dotnet}`))
  assert.ok(workflow.includes(`node-version: ${toolchain.node}`))
  for (const file of ['../../quality/package-lock.json', '../../ui/package-lock.json']) {
    const lock = JSON.parse(await readFile(new URL(file, import.meta.url), 'utf8'))
    assert.equal(lock.packages['node_modules/@playwright/test'].version, toolchain.playwright)
    const browsers = Object.entries(lock.packages)
      .filter(([path]) => /(?:^|\/)node_modules\/playwright(?:-core)?$/.test(path))
    assert.ok(browsers.length > 0, `Missing browser runtime in ${file}`)
    for (const [path, dependency] of browsers) {
      assert.equal(dependency.version, toolchain.playwright, `${file}: ${path} differs from the browser image.`)
    }
  }
})
