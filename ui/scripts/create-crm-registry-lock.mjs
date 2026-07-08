#!/usr/bin/env node

import { execFileSync } from 'node:child_process'
import { cp, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const uiRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const repositoryRoot = resolve(uiRoot, '..')
const crmRoot = join(uiRoot, 'ngb-crm-web')
const crmManifest = JSON.parse(await readFile(join(crmRoot, 'package.json'), 'utf8'))
const packageName = '@ngbplatform/ui'
const version = crmManifest.dependencies[packageName]
const tarball = join(repositoryRoot, 'artifacts', 'npm', `ngbplatform-ui-${version}.tgz`)
const registryTarball = `https://registry.npmjs.org/@ngbplatform/ui/-/ui-${version}.tgz`
const stagingRoot = await mkdtemp(join(tmpdir(), 'ngb-crm-lock-'))

try {
  await cp(join(crmRoot, 'package.json'), join(stagingRoot, 'package.json'))
  await cp(join(crmRoot, '.npmrc'), join(stagingRoot, '.npmrc'))

  execFileSync(
    process.platform === 'win32' ? 'npm.cmd' : 'npm',
    [
      'install',
      '--package-lock-only',
      '--ignore-scripts',
      '--no-audit',
      tarball,
    ],
    {
      cwd: stagingRoot,
      env: {
        ...process.env,
        npm_config_cache: join(stagingRoot, '.npm-cache'),
      },
      stdio: 'inherit',
    },
  )

  const lockPath = join(stagingRoot, 'package-lock.json')
  const lock = JSON.parse(await readFile(lockPath, 'utf8'))
  const rootPackage = lock.packages?.['']
  const packageEntry = lock.packages?.[`node_modules/${packageName}`]

  if (!rootPackage || !packageEntry) {
    throw new Error(`npm did not create a lock entry for ${packageName}.`)
  }

  if (packageEntry.version !== version || !packageEntry.integrity) {
    throw new Error(`Unexpected ${packageName} lock metadata.`)
  }

  rootPackage.dependencies[packageName] = version
  packageEntry.resolved = registryTarball

  await writeFile(
    join(crmRoot, 'package-lock.json'),
    `${JSON.stringify(lock, null, 2)}\n`,
    'utf8',
  )
} finally {
  await rm(stagingRoot, { recursive: true, force: true })
}
