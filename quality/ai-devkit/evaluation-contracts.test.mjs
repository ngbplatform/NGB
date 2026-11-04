import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import { digest } from '../../packaging/templates/content/infrastructure/ngb/kit-contracts.mjs'

const matrix = JSON.parse(await readFile(new URL('./agent-evaluations.json', import.meta.url)))

for (const [id, fixture] of [['existing-document-change', 'document-change'], ['new-catalog', 'new-catalog'],
  ['report-extension', 'report-extension'], ['permission-correction', 'permission-correction'],
  ['customized-kit-upgrade', 'customized-kit-upgrade']]) {
  test(`${id} evaluates exactly the published task contract without hidden prompt requirements`, async () => {
    const contract = JSON.parse(await readFile(new URL(`./evaluation-fixtures/${fixture}.contract.json`, import.meta.url)))
    const task = matrix.tasks.find(item => item.id === id)
    assert.equal(contract.task, id)
    assert.equal(task.prompt, contract.prompt)
    assert.deepEqual(task.acceptance, contract.acceptance)
    assert.ok(Object.keys(contract.observableContract).length > 0)
  })
}

test('report and permission starting fixtures retain their frozen application overlays', async () => {
  for (const name of ['report-seed', 'permission-seed']) {
    const seed = JSON.parse(await readFile(new URL(`./evaluation-fixtures/${name}.json`, import.meta.url)))
    assert.equal(seed.schemaVersion, 1)
    assert.ok(Object.keys(seed.files).length > 0)
    for (const [path, hash] of Object.entries(seed.files)) {
      const bytes = await readFile(new URL(`./evaluation-fixtures/${name}/${path}`, import.meta.url))
      assert.equal(digest(bytes), hash, `Starting fixture changed: ${name}/${path}`)
    }
  }
})
