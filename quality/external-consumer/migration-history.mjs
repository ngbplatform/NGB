import assert from 'node:assert/strict'

export function assertMigrationHistoryPreserved(source, target, platformMigrations) {
  const histories = state => Object.fromEntries(Object.entries(state)
    .filter(([name]) => name.startsWith('migration_changelog__')))
  const before = histories(source)
  const after = histories(target)
  const platform = 'migration_changelog__platform'
  assert.ok(Object.hasOwn(before, platform), 'Missing source platform migration history.')
  assert.deepEqual(Object.keys(after).sort(), Object.keys(before).sort(), 'Migration history tables changed.')
  assert.ok(platformMigrations.length > 0, 'The candidate must contain platform migrations.')

  for (const [table, text] of Object.entries(before)) {
    if (table !== platform) {
      assert.equal(after[table], text, `Application migration history changed: ${table}`)
      continue
    }

    const previous = JSON.parse(text)
    const current = JSON.parse(after[table])
    const previousIds = new Set(previous.map(row => row.id))
    const currentById = new Map(current.map(row => [row.id, row]))
    assert.equal(previousIds.size, previous.length, 'Duplicate source migration IDs.')
    assert.equal(currentById.size, current.length, 'Duplicate target migration IDs.')
    for (const row of previous) {
      assert.deepEqual(currentById.get(row.id), row, `Applied migration changed or disappeared: ${row.name}`)
    }

    const lastId = Math.max(0, ...previousIds)
    for (const row of current.filter(row => !previousIds.has(row.id))) {
      assert.ok(Number.isSafeInteger(row.id) && row.id > lastId, 'New migrations must append to the history.')
      assert.equal(row.type, 0, 'Only versioned platform migrations may extend the history.')
      assert.equal(row.success, true, `Platform migration failed: ${row.name}`)
    }

    const identities = rows => rows.map(({ name, version, checksum }) => ({ name, version, checksum }))
      .sort((left, right) => left.name.localeCompare(right.name, 'en'))
    assert.deepEqual(identities(current.filter(row => row.type === 0)), identities(platformMigrations),
      'Applied platform migrations differ from the candidate package.')
  }

  return after
}
