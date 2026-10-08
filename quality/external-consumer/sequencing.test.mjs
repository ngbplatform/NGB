import assert from 'node:assert/strict'
import test from 'node:test'
import { startAfterSuccessfulMigration } from './sequencing.mjs'

test('writers start only after a successfully completed migration', async () => {
  const events = []
  const result = await startAfterSuccessfulMigration(
    async () => { events.push('migrate') },
    async () => { events.push('start'); return 'running' },
  )
  assert.deepEqual(events, ['migrate', 'start'])
  assert.equal(result, 'running')
  await assert.rejects(startAfterSuccessfulMigration(
    async () => { throw new Error('migration failed') },
    async () => { events.push('forbidden start') },
  ), /migration failed/)
  assert.deepEqual(events, ['migrate', 'start'])
  await assert.rejects(startAfterSuccessfulMigration(
    async () => {},
    async () => { throw new Error('host failed') },
  ), /host failed/)
})
