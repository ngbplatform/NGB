import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { verifyCliArchive } from '../../packaging/templates/content/infrastructure/ngb/archive.mjs'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'
import { readBytes } from '../../packaging/templates/content/infrastructure/ngb/kit-files.mjs'

export async function evaluationInputs(repository, candidate) {
  const root = join(repository, 'quality/ai-devkit')
  const matrixBytes = await readFile(join(root, 'agent-evaluations.json'))
  const matrix = JSON.parse(matrixBytes)
  const cli = verifyCliArchive(candidate.files[candidate.cli], candidate.version)
  const inputs = {
    matrixSha256: digest(matrixBytes),
    version: candidate.version,
    kitSha256: digest(cli.files.get('package/infrastructure/ngb/kit-payload.json')),
    cliSha256: digest(candidate.files[candidate.cli]),
    templateSha256: digest(candidate.files[candidate.template]),
    artifactsSha256: Object.fromEntries(Object.entries(candidate.files).map(([path, bytes]) => [path, digest(bytes)])),
    packages: Object.fromEntries(Object.entries(candidate.files)
      .filter(([path]) => path.endsWith('.nupkg') && path !== candidate.template || path === candidate.ui)
      .map(([path, bytes]) => [path, digest(bytes)])),
    oracles: {},
    evaluators: {},
    acceptanceRunners: {},
    fixturePreparers: {},
    acceptanceSupport: {},
  }
  for (const task of matrix.tasks) {
    inputs.oracles[task.id] = digest(await readFile(join(root, `evaluation-oracles/${task.id}.mjs`)))
  }
  for (const client of matrix.clients) {
    inputs.evaluators[client] = {}
    for (const family of ['discovery', 'development', 'upgrade']) {
      const profile = await readBytes(root, `${client}-${family}-profile.json`)
      const runner = await readBytes(root, `evaluate-${client}-${family}.mjs`)
      inputs.evaluators[client][family] = profile && runner ? {
        clientVersion: JSON.parse(profile).clientVersion,
        profileSha256: digest(profile),
        runnerSha256: digest(runner),
        outputRedactorSha256: digest(await readFile(join(root, 'agent-output.mjs'))),
      } : null
    }
  }
  for (const family of ['development', 'upgrade']) {
    inputs.acceptanceRunners[family] = digest(await readFile(join(root, `run-${family}-oracle.mjs`)))
    const preparer = family === 'development' ? 'prepare-development-fixture.mjs' : 'prepare-upgrade-evaluation.mjs'
    inputs.fixturePreparers[family] = digest(await readFile(join(root, preparer)))
  }
  for (const path of ['evaluation-data.mjs', 'evaluation-inputs.mjs', 'example-evidence.mjs', 'development-evidence.mjs', 'verify-agents.mjs']) {
    inputs.acceptanceSupport[path] = digest(await readFile(join(root, path)))
  }
  return { matrix, inputs }
}
