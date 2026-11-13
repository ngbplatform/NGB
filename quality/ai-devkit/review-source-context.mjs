import assert from 'node:assert/strict'
import { execFile } from 'node:child_process'
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises'
import { devNull, tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { promisify } from 'node:util'
import { digest, portablePath } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'

const exec = promisify(execFile)

export async function reviewSourceContext(changed, files, originals) {
  const context = { sources: {}, originalSources: {}, testChanges: {} }
  const workspace = await mkdtemp(join(tmpdir(), 'ngb-review-sources-'))
  try {
    for (const path of changed) {
      portablePath(path)
      const source = files[path].toString('utf8')
      const original = originals[path] ?? null
      const existingTest = original !== null
        && /^(?:EquipmentInspection\.(?:IntegrationTests|Tests)\/.+\.cs|web\/tests\/.+\.ts)$/.test(path)
      if (!existingTest) {
        context.sources[path] = source
        context.originalSources[path] = original
        continue
      }
      // The original is a hash-checked trusted fixture. Preserve every addition,
      // deletion and whitespace change; omit only unchanged lines outside context.
      for (const [side, text] of [['before', original], ['after', source]]) {
        const target = join(workspace, side, path)
        await mkdir(dirname(target), { recursive: true })
        await writeFile(target, text, { mode: 0o600 })
      }
      let patch = ''
      try {
        await exec('git', ['--no-pager', 'diff', '--no-index', '--no-ext-diff', '--no-textconv', '--no-color',
          '--text', '--unified=12', '--src-prefix=a/', '--dst-prefix=b/', '--', `before/${path}`, `after/${path}`], {
          cwd: workspace, timeout: 15000, maxBuffer: 16 * 1024 * 1024,
          env: { PATH: process.env.PATH, LANG: 'C', GIT_CONFIG_NOSYSTEM: '1',
            GIT_CONFIG_GLOBAL: devNull, GIT_ATTR_NOSYSTEM: '1' },
        })
      } catch (error) {
        if (error.code !== 1 || typeof error.stdout !== 'string') throw error
        patch = error.stdout
      }
      assert.ok(patch.length > 0, `Changed test has no source diff: ${path}`)
      context.testChanges[path] = { beforeSha256: digest(original), afterSha256: digest(files[path]), patch }
    }
    return context
  } finally {
    await rm(workspace, { recursive: true, force: true })
  }
}
