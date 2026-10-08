import { execFileSync } from 'node:child_process'
import { cp, mkdir, mkdtemp } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const artifacts = resolve(process.argv[2] ?? join(repository, 'artifacts'))
const run = (command, args, options = {}) => execFileSync(command, args, { cwd: repository, stdio: 'inherit', ...options })

if (process.platform === 'linux') {
  run('bash', ['run-full-quality.sh'])
} else {
  // The full browser matrix uses the same Linux browser dependencies as CI.
  // Copy sources rather than mounting host node_modules, bin or obj into Linux.
  const root = await mkdtemp(join(tmpdir(), 'ngb-full-quality-'))
  const paths = execFileSync('git', ['ls-files', '--cached', '--others', '--exclude-standard', '-z'], { cwd: repository, encoding: 'utf8' })
    .split('\0').filter(path => path && path !== 'AGENTS.md' && !path.endsWith('.DS_Store'))
  for (const path of paths) {
    await mkdir(dirname(join(root, path)), { recursive: true })
    await cp(join(repository, path), join(root, path))
  }
  await cp(join(artifacts, 'nuget-release'), join(root, 'artifacts/nuget'), { recursive: true })
  await cp(join(artifacts, 'npm'), join(root, 'artifacts/npm'), { recursive: true })
  run('docker', ['build', '-t', 'ngb-certification-quality:local', '-f', 'quality/external-consumer/Dockerfile.quality', 'quality/external-consumer'])
  const endpoint = execFileSync('docker', ['context', 'inspect', '--format', '{{.Endpoints.docker.Host}}'], { encoding: 'utf8' }).trim()
  if (!endpoint.startsWith('unix://')) throw new Error('Local full quality requires a Unix Docker socket; run the Linux CI workflow for a remote daemon.')
  run('docker', [
    'run', '--rm', '--init', '--ipc=host',
    '-e', 'TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal',
    '-e', 'NGB_FRONTEND_COVERAGE_NO_INSTALL=true',
    '-v', `${endpoint.slice('unix://'.length)}:/var/run/docker.sock`,
    '-v', `${root}:/workspace`, '-w', '/workspace', 'ngb-certification-quality:local',
    'bash', '-euc', [
      'npm cache add artifacts/npm/*.tgz',
      'npm --prefix ui ci',
      'npm --prefix quality ci',
      'bash run-full-quality.sh',
    ].join('\n'),
  ])
  await cp(join(root, 'artifacts/coverage'), join(repository, 'artifacts/coverage'), { recursive: true })
  await cp(join(root, 'artifacts/certification'), join(repository, 'artifacts/certification'), { recursive: true })
  console.log(`Complete Linux quality diagnostics: ${root}`)
}
