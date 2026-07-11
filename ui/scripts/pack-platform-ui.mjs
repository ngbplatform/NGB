#!/usr/bin/env node

import { execFileSync } from 'node:child_process'
import { cp, mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const uiRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const sourceRoot = join(uiRoot, 'ngb-ui-framework')
const outputRoot = resolve(uiRoot, '..', 'artifacts', 'npm')
const sourceManifest = JSON.parse(await readFile(join(sourceRoot, 'package.json'), 'utf8'))
const requestedVersion = readVersionArgument(process.argv.slice(2)) ?? sourceManifest.version

if (requestedVersion !== sourceManifest.version) {
  throw new Error(
    `Requested version ${requestedVersion} does not match ngb-ui-framework version ${sourceManifest.version}.`,
  )
}

const packageManifest = {
  name: '@ngbplatform/ui',
  version: requestedVersion,
  description: 'Reusable Vue UI building blocks for NGB Platform vertical applications.',
  keywords: ['ngb', 'ngb-platform', 'vue', 'ui-framework'],
  license: 'Apache-2.0',
  author: 'NGB Platform',
  homepage: 'https://ngbplatform.com',
  repository: {
    type: 'git',
    url: 'https://github.com/ngbplatform/NGB.git',
    directory: 'ui/ngb-ui-framework',
  },
  bugs: {
    url: 'https://github.com/ngbplatform/NGB/issues',
  },
  type: 'module',
  engines: {
    node: '>=22.14.0',
  },
  sideEffects: ['./src/styles/tailwind.css', './src/**/*.vue'],
  exports: {
    '.': {
      types: './src/index.ts',
      import: './src/index.ts',
      default: './src/index.ts',
    },
    './styles': './src/styles/tailwind.css',
    './vite-public-assets': './vite-public-assets.js',
  },
  files: ['LICENSE', 'README.md', 'public', 'src', 'vite-public-assets.js'],
  publishConfig: {
    access: 'public',
    registry: 'https://registry.npmjs.org/',
  },
  dependencies: {
    '@headlessui/vue': sourceManifest.dependencies['@headlessui/vue'],
    echarts: sourceManifest.dependencies.echarts,
    'vue-echarts': sourceManifest.dependencies['vue-echarts'],
  },
  peerDependencies: {
    'keycloak-js': sourceManifest.dependencies['keycloak-js'],
    pinia: sourceManifest.dependencies.pinia,
    vue: sourceManifest.dependencies.vue,
    'vue-router': sourceManifest.dependencies['vue-router'],
  },
}

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

  await cp(
    join(outputRoot, `ngbplatform-ui-${requestedVersion}.tgz`),
    join(outputRoot, 'ngbplatform-ui-local.tgz'),
  )
} finally {
  await rm(stagingRoot, { recursive: true, force: true })
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
