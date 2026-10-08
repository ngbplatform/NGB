import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { mkdtemp, readFile, readdir, stat, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import ts from 'typescript'
import { compareCssTokens, comparePackageContracts, compareTypeContracts } from './platform-ui-compatibility.mjs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const matrix = JSON.parse(await readFile(join(repository, 'quality/external-consumer/matrix.json'), 'utf8'))
const archive = resolve(process.argv[2] ?? join(repository, 'artifacts/npm', `ngbplatform-ui-${matrix.target}.tgz`))
const root = await mkdtemp(join(tmpdir(), 'ngb-ui-compatibility-'))
const manifest = {
  private: true,
  type: 'module',
  dependencies: {
    '@ngbplatform/ui': `file:${archive}`,
    '@ngbplatform/ui-baseline': `npm:@ngbplatform/ui@${matrix.source}`,
    vue: '3.5.30',
    'vue-router': '4.6.4',
    pinia: '3.0.4',
    'keycloak-js': '26.2.3',
    tailwindcss: '3.4.19',
    typescript: '5.9.3',
    'vue-tsc': '3.3.9',
    vite: '7.3.7',
    '@types/node': '24.12.0',
  },
}
await writeFile(join(root, 'package.json'), `${JSON.stringify(manifest, null, 2)}\n`)
execFileSync('npm', ['install', '--ignore-scripts', '--workspaces=false', '--cache', join(root, 'npm-cache'), '--registry', 'https://registry.npmjs.org'], { cwd: root, stdio: 'inherit' })
const oldRoot = join(root, 'node_modules/@ngbplatform/ui-baseline')
const nextRoot = join(root, 'node_modules/@ngbplatform/ui')
const json = async filename => JSON.parse(await readFile(filename, 'utf8'))
const previous = await json(join(oldRoot, 'package.json'))
const candidate = await json(join(nextRoot, 'package.json'))
const errors = comparePackageContracts(previous, candidate)
errors.push(...compareCssTokens(await readFile(join(oldRoot, 'src/styles/tailwind.css'), 'utf8'), await readFile(join(nextRoot, 'src/styles/tailwind.css'), 'utf8')))

async function verifyAssets(oldDirectory, newDirectory) {
  for (const name of await readdir(oldDirectory)) {
    const oldFile = join(oldDirectory, name)
    const newFile = join(newDirectory, name)
    if ((await stat(oldFile)).isDirectory()) await verifyAssets(oldFile, newFile)
    else await stat(newFile)
  }
}
await verifyAssets(join(oldRoot, 'public'), join(nextRoot, 'public'))
await verifyAssets(join(oldRoot, 'src/assets'), join(nextRoot, 'src/assets'))
for (const [name, directory] of [['previous', oldRoot], ['candidate', nextRoot]]) {
  const config = {
    compilerOptions: {
      target: 'ES2022', module: 'ESNext', moduleResolution: 'Bundler', strict: true,
      skipLibCheck: true, jsx: 'preserve', allowJs: true, declaration: true, emitDeclarationOnly: true,
      types: ['vite/client', 'node'], rootDir: directory, outDir: join(root, name),
    },
    include: [join(directory, 'src/**/*'), join(directory, 'vite-public-assets.js')],
  }
  await writeFile(join(root, `${name}.json`), `${JSON.stringify(config, null, 2)}\n`)
  execFileSync(process.execPath, ['node_modules/vue-tsc/bin/vue-tsc.js', '-p', `${name}.json`], { cwd: root, stdio: 'inherit' })
}
for (const [subpath, value] of Object.entries(previous.exports)) {
  const target = typeof value === 'string' ? value : value.types
  if (!target || target.endsWith('.css')) continue
  const declaration = target.replace(/\.(?:ts|js)$/, '.d.ts')
  const oldFile = join(root, 'previous', declaration)
  const newFile = join(root, 'candidate', declaration)
  const program = ts.createProgram([oldFile, newFile], {
    target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext,
    moduleResolution: ts.ModuleResolutionKind.Bundler, strict: true, skipLibCheck: true,
    noEmit: true, types: [],
    paths: {
      '@ngbplatform/ui-baseline': [join(root, 'previous/src/index.d.ts')],
      '@ngbplatform/ui': [join(root, 'candidate/src/index.d.ts')],
    },
  })
  errors.push(...compareTypeContracts(program, oldFile, newFile).map(error => `${subpath}: ${error}`))
}
if (errors.length) throw new Error(`Incompatible npm contracts:\n${errors.join('\n')}`)
await writeFile(join(dirname(archive), 'npm-compatibility.json'), `${JSON.stringify({ schemaVersion: 1, artifactSha256: createHash('sha256').update(await readFile(archive)).digest('hex'), previous: previous.version, candidate: candidate.version, entryPoints: Object.keys(previous.exports), status: 'passed' }, null, 2)}\n`)
console.log(`Verified all ${Object.keys(previous.exports).length} previous npm entry points, declarations, peers, CSS tokens and assets.`)
