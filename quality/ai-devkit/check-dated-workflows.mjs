import assert from 'node:assert/strict'
import { execFileSync, spawnSync } from 'node:child_process'
import { createHash, randomUUID } from 'node:crypto'
import { readFile } from 'node:fs/promises'
import { setTimeout } from 'node:timers/promises'

const args = process.argv.slice(2)
assert.ok(args.length === 0 || (args.length === 2 && args[0] === '--candidate'),
  'Usage: node check-dated-workflows.mjs [--candidate reviewed-migration.sql]')
const candidate = args.length ? await readFile(args[1], 'utf8') : null
if (candidate !== null) assert.ok(candidate.trim(), 'The candidate migration is empty.')
const fixture = await readFile(new URL('../../ai-devkit/examples/equipment-inspection/TemporalFixtures/work-calendar.sql', import.meta.url), 'utf8')
const oracle = await readFile(new URL('./regressions/dated-workflows.sql', import.meta.url), 'utf8')
const container = 'ngb-timeline-' + randomUUID()
const mutations = [
  ['entitlements < 0', 'false', 'future entitlement accepted'],
  ['WHERE funds < 0', 'WHERE false', 'future funds accepted'],
  ['WHERE funds < 0 OR entitlements < 0', 'WHERE withdraw IS NULL AND (funds < 0 OR entitlements < 0)', 'late replacement accepted'],
  ['business_date <= cutoff', "((business_date + time '12:00') AT TIME ZONE 'UTC')::date <= cutoff", 'business date shifted'],
  ['a.id <> NEW.id', 'a.id <> NEW.id AND a.finished IS NULL', 'closed overlap accepted'],
  ['BEFORE INSERT OR UPDATE OF resource_id, started, finished', 'BEFORE INSERT', 'closing edit created overlap'],
  ['CHECK (finished > started)', 'CHECK (finished >= started)', 'zero duration accepted'],
  ['started AT TIME ZONE captured_zone', "started AT TIME ZONE 'UTC'", 'changed calendar'],
  ['SELECT id, resource_id, captured_category,', 'SELECT id, resource_id, (SELECT category FROM work_example.resource r WHERE r.id = resource_id) AS captured_category,', 'changed category'],
  ['(OLD.retired AND NOT NEW.retired)', 'false', 'reactivated identity'],
  ['NEW.stable_code <> OLD.stable_code', 'false', 'mutable identifier'],
  ["CHECK (contact ~ '[^[:space:]]')", '', 'blank contact'],
  ["CHECK (contact ~ '[^[:space:]]')", "CHECK (btrim(contact) <> '')", 'whitespace-only contact'],
]

function evaluate(source) {
  return spawnSync('docker', ['exec', '-i', container, 'psql', '-X', '-q', '-v', 'ON_ERROR_STOP=1', '-U', 'postgres'],
    { input: ['BEGIN;', source, oracle, 'ROLLBACK;'].join('\n'), encoding: 'utf8', timeout: 30000 })
}

execFileSync('docker', ['run', '--detach', '--rm', '--name', container, '--network', 'none',
  '--memory', '320m', '--pids-limit', '100', '--tmpfs', '/var/lib/postgresql/data', '--env', 'POSTGRES_HOST_AUTH_METHOD=trust', 'postgres:16-alpine'])
try {
  for (let i = 0; i < 100; i++) {
    const ready = spawnSync('docker', ['exec', container, 'pg_isready', '-h', '127.0.0.1', '-U', 'postgres'])
    if (ready.status === 0) break
    await setTimeout(250)
  }
  const correct = evaluate(candidate ?? fixture)
  assert.equal(correct.status, 0, correct.error?.message ?? correct.stderr)
  if (candidate !== null) {
    const sha256 = createHash('sha256').update(candidate).digest('hex')
    console.log('Dated workflow regression: candidate passed (' + sha256 + ').')
  } else {
    for (const [original, replacement, message] of mutations) {
      assert.equal(fixture.split(original).length, 2, 'Update mutation anchor: ' + original)
      const result = evaluate(fixture.replace(original, replacement))
      assert.equal(result.status, 3, result.error?.message ?? result.stderr)
      assert.ok(result.stderr.includes('ERROR:  oracle: ' + message), result.stderr)
    }
    console.log('Dated workflow regression: reference passed and all ' + mutations.length + ' faulty implementations rejected.')
  }
} finally {
  execFileSync('docker', ['container', 'stop', '--time', '1', container])
}
