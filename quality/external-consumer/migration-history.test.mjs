import assert from 'node:assert/strict'
import test from 'node:test'
import { assertMigrationHistoryPreserved } from './migration-history.mjs'

const platform = 'migration_changelog__platform'
const application = 'migration_changelog__certification'

function migration(id, version) {
  return {
    id, name: `V${version.replaceAll('.', '_')}__fixture.sql`, type: 0, success: true,
    version, checksum: String(id).repeat(32), description: 'fixture (2 ms)',
    installed_by: 'postgres', installed_on: '2026-11-11T12:00:00',
  }
}

function fixture() {
  const schema = { id: 1, name: 'public', type: 2, success: true, version: '0', checksum: '' }
  const previous = [schema, migration(2, '2026.10.01.0100')]
  const added = [migration(9, '2026.11.08.0100'), migration(10, '2026.11.09.0100')]
  const source = { [platform]: JSON.stringify(previous), [application]: JSON.stringify([migration(1, '2026.10.06.0100')]) }
  const rows = [...previous, ...added]
  const expected = rows.filter(row => row.type === 0).map(({ name, version, checksum }) => ({ name, version, checksum }))
  const target = { ...source, [platform]: JSON.stringify(rows) }
  return { source, target, rows, expected }
}

test('preserves applied rows while admitting exactly the packaged migrations regardless of snapshot order', () => {
  const { source, target, rows, expected } = fixture()
  target[platform] = JSON.stringify([rows[0], rows[3], rows[1], rows[2]])
  source.catalogs = '[{"id":"before"}]'
  target.catalogs = '[{"id":"after"}]'
  assert.deepEqual(assertMigrationHistoryPreserved(source, target, expected), {
    [platform]: target[platform], [application]: target[application],
  })
  assertMigrationHistoryPreserved(target, target, expected)
})

test('accepts the first complete platform history when the source history is empty', () => {
  const { source, target, rows, expected } = fixture()
  source[platform] = '[]'
  target[platform] = JSON.stringify(rows.filter(row => row.type === 0))
  assertMigrationHistoryPreserved(source, target, expected)
})

test('rejects missing histories, changed table inventories and an empty package inventory', () => {
  for (const change of [
    value => { delete value.source[platform] },
    value => { delete value.target[platform] },
    value => { value.target.migration_changelog__unexpected = '[]' },
    value => { value.expected = [] },
    value => { value.target[application] = '[]' },
  ]) {
    const value = fixture()
    change(value)
    assert.throws(() => assertMigrationHistoryPreserved(value.source, value.target, value.expected))
  }
})

test('rejects rewriting or deleting any field of an already applied migration or schema marker', () => {
  for (const field of ['id', 'name', 'type', 'success', 'version', 'checksum', 'description', 'installed_by', 'installed_on']) {
    const { source, target, rows, expected } = fixture()
    rows[1][field] = 'changed'
    target[platform] = JSON.stringify(rows)
    assert.throws(() => assertMigrationHistoryPreserved(source, target, expected), /changed or disappeared/)
  }
  for (const index of [0, 1]) {
    const { source, target, rows, expected } = fixture()
    rows.splice(index, 1)
    target[platform] = JSON.stringify(rows)
    assert.throws(() => assertMigrationHistoryPreserved(source, target, expected), /changed or disappeared/)
  }
})

test('rejects duplicate source and target migration identities', () => {
  for (const side of ['source', 'target']) {
    const { source, target, expected } = fixture()
    const state = side === 'source' ? source : target
    const rows = JSON.parse(state[platform])
    rows.push(rows[1])
    state[platform] = JSON.stringify(rows)
    assert.throws(() => assertMigrationHistoryPreserved(source, target, expected), /Duplicate/)
  }
})

test('rejects failed, non-versioned and non-appended migration entries', () => {
  for (const override of [{ id: 0 }, { id: 9.5 }, { id: '9' }, { type: 1 }, { type: 2 }, { success: false }]) {
    const { source, target, rows, expected } = fixture()
    Object.assign(rows[2], override)
    target[platform] = JSON.stringify(rows)
    assert.throws(() => assertMigrationHistoryPreserved(source, target, expected))
  }
})

test('rejects missing, unexpected, renamed, renumbered and checksum-altered packaged migrations', () => {
  for (const change of [
    rows => { rows.splice(2, 1) },
    rows => { rows.push(migration(11, '2026.11.10.0100')) },
    rows => { rows[2].name = 'unexpected.sql' },
    rows => { rows[2].version = '2026.11.08.0200' },
    rows => { rows[2].checksum = 'different' },
    rows => { rows[3] = { ...rows[2], id: rows[3].id } },
  ]) {
    const { source, target, rows, expected } = fixture()
    change(rows)
    target[platform] = JSON.stringify(rows)
    assert.throws(() => assertMigrationHistoryPreserved(source, target, expected), /differ from the candidate package/)
  }
})
