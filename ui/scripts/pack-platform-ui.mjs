#!/usr/bin/env node

import { execFileSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { cp, mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import { gzipSync, gunzipSync } from 'fflate'

import { createPlatformUiPackageManifest } from './platform-ui-package-manifest.mjs'

const uiRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const sourceRoot = join(uiRoot, 'ngb-ui-framework')
const crmRoot = join(uiRoot, 'ngb-crm-web')
const outputRoot = resolve(uiRoot, '..', 'artifacts', 'npm')
const sourceManifest = JSON.parse(await readFile(join(sourceRoot, 'package.json'), 'utf8'))
const requestedVersion = readVersionArgument(process.argv.slice(2)) ?? sourceManifest.version

if (requestedVersion !== sourceManifest.version) {
  throw new Error(
    `Requested version ${requestedVersion} does not match @ngbplatform/ui source version ${sourceManifest.version}.`,
  )
}

const packageManifest = createPlatformUiPackageManifest(sourceManifest, requestedVersion)

const stagingRoot = await mkdtemp(join(tmpdir(), 'ngb-platform-ui-'))
const packageRoot = join(stagingRoot, 'package')

try {
  await mkdir(packageRoot, { recursive: true })

  for (const entry of packageManifest.files) {
    await cp(join(sourceRoot, entry), join(packageRoot, entry), {
      recursive: true,
      force: true,
    })
  }

  await writeFile(
    join(packageRoot, 'package.json'),
    `${JSON.stringify(packageManifest, null, 2)}\n`,
    'utf8',
  )

  await mkdir(outputRoot, { recursive: true })
  for (const entry of await readdir(outputRoot)) {
    if (entry.startsWith('ngbplatform-ui-') && entry.endsWith('.tgz')) {
      await rm(join(outputRoot, entry), { force: true })
    }
  }

  execFileSync(
    process.platform === 'win32' ? 'npm.cmd' : 'npm',
    ['pack', packageRoot, '--pack-destination', outputRoot],
    {
      cwd: uiRoot,
      env: {
        ...process.env,
        npm_config_cache: join(stagingRoot, '.npm-cache'),
      },
      stdio: 'inherit',
    },
  )

  const tarballPath = join(outputRoot, `ngbplatform-ui-${requestedVersion}.tgz`)
  await normalizeTarballCompression(tarballPath)
  console.log(`Deterministic package integrity: ${await calculateIntegrity(tarballPath)}`)

  await cp(
    tarballPath,
    join(outputRoot, 'ngbplatform-ui-local.tgz'),
  )
  await verifyCrmConsumerLock(tarballPath, requestedVersion)
} finally {
  await rm(stagingRoot, { recursive: true, force: true })
}

async function normalizeTarballCompression(tarballPath) {
  const npmTarball = await readFile(tarballPath)
  const tarArchive = gunzipSync(npmTarball)

  // npm pack delegates gzip compression to the host runtime. The resulting
  // gzip byte stream can differ across operating systems even when the TAR
  // archive is identical. Recompress with a pinned pure-JavaScript codec so
  // local validation and the Linux publication workflow produce the same
  // registry integrity.
  const deterministicTarball = gzipSync(tarArchive, {
    level: 9,
    mtime: 0,
  })

  await writeFile(tarballPath, deterministicTarball)
}

async function verifyCrmConsumerLock(tarballPath, version) {
  const crmManifest = JSON.parse(await readFile(join(crmRoot, 'package.json'), 'utf8'))
  const crmLock = JSON.parse(await readFile(join(crmRoot, 'package-lock.json'), 'utf8'))
  const locked = crmLock.packages?.['node_modules/@ngbplatform/ui']
  const expectedResolved = `https://registry.npmjs.org/@ngbplatform/ui/-/ui-${version}.tgz`
  const expectedIntegrity = await calculateIntegrity(tarballPath)

  if (crmManifest.dependencies?.['@ngbplatform/ui'] !== version) {
    throw new Error(`CRM must reference @ngbplatform/ui ${version} exactly.`)
  }
  if (
    crmLock.packages?.['']?.dependencies?.['@ngbplatform/ui'] !== version
    || locked?.version !== version
    || locked?.resolved !== expectedResolved
    || locked?.integrity !== expectedIntegrity
  ) {
    throw new Error(
      `CRM package-lock must reference the exact @ngbplatform/ui ${version} release candidate `
        + 'including its registry URL and SHA-512 integrity.',
    )
  }
}

async function calculateIntegrity(tarballPath) {
  return `sha512-${createHash('sha512')
    .update(await readFile(tarballPath))
    .digest('base64')}`
}

function readVersionArgument(args) {
  const versionIndex = args.indexOf('--version')
  if (versionIndex < 0) {
    return undefined
  }

  const value = args[versionIndex + 1]?.trim()
  if (!value) {
    throw new Error('Expected a semantic version after --version.')
  }

  return value
}
