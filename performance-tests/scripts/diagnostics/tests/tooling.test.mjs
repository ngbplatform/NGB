import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { InMemoryTransport } from '@modelcontextprotocol/sdk/inMemory.js';
import { createServer, samplingSql, contentionSql } from '../postgres-server.mjs';
import { databaseEnvironment, loadEnvironment, parseOptions, workloadEnvironment } from '../config.mjs';

test('dotenv is data, never shell code; inherited values win', () => {
  const root = mkdtempSync(join(tmpdir(), 'ngb-env-'));
  try {
    const path = join(root, 'local.env');
    writeFileSync(path, 'VALUE="$(touch /tmp/do-not-execute)"\nNUMBER=7\n');
    assert.equal(loadEnvironment(path, {}).VALUE, '$(touch /tmp/do-not-execute)');
    assert.equal(loadEnvironment(path, { NUMBER: '9' }).NUMBER, '9');
  } finally { rmSync(root, { recursive: true, force: true }); }
});

test('capacity explicitly enables real writes and recording modes remain predictable', () => {
  const env = workloadEnvironment('platform-mixed-capacity', { NGB_PERF_ENABLE_WRITES: 'false' });
  assert.equal(env.NGB_PERF_ENABLE_WRITES, 'true');
  assert.equal(env.NGB_PERF_ENABLE_POSTING, 'true');
  assert.equal(env.NGB_PERF_ENABLE_PERIOD_CLOSE, 'false');
  assert.equal(env.NGB_PM_POSTING_MODE, 'fresh');
  assert.equal(env.NGB_CAPACITY_VUS, '80,160,240,320');
  assert.equal(workloadEnvironment('platform-read-capacity', {}).NGB_PERF_ENABLE_WRITES, 'false');
  assert.equal(workloadEnvironment('platform-mixed-capacity', { NGB_CAPACITY_VUS: '10,20' }).NGB_CAPACITY_VUS, '10,20');
  assert.throws(() => workloadEnvironment('platform-mixed-capacity', { NGB_CAPACITY_VUS: '-1,80' }));
  assert.throws(() => parseOptions(['--profile', '../../bad']));
  assert.throws(() => parseOptions(['--profile', 'write-heavy', '--unknown']));
});

test('database credentials stay in the child environment and are not shell-interpreted', () => {
  const root = mkdtempSync(join(tmpdir(), 'ngb-db-env-'));
  try {
    const path = join(root, 'local.env');
    writeFileSync(path, 'POSTGRES_ADMIN_USER=reader\nPOSTGRES_ADMIN_PASSWORD="a$(literal);password"\nPM_DB_NAME=test\nPOSTGRES_HOST_PORT=5433\n');
    const env = databaseEnvironment(path, { PATH: '/bin' });
    assert.equal(env.PGPASSWORD, 'a$(literal);password');
    assert.equal(env.PGPORT, '5433');
    assert.equal(env.PGHOST, 'localhost');
    assert.equal(env.PGDATABASE, 'test');
    assert.equal(env.PGUSER, 'reader');
    assert.equal(databaseEnvironment(path, { PGUSER: 'override' }).PGUSER, 'override');
    assert.throws(() => databaseEnvironment(path, { PGPORT: '99999' }));
  } finally { rmSync(root, { recursive: true, force: true }); }
});

async function withServer(query, action) {
  const statements = [];
  let releases = 0;
  let destroyed = false;
  const pool = { connect: async () => ({ query: async value => {
    statements.push(value);
    return query(value);
  }, release: discard => { releases++; destroyed = discard; } }) };
  const server = createServer(pool);
  const client = new Client({ name: 'test', version: '1.0.0' });
  const [a, b] = InMemoryTransport.createLinkedPair();
  await server.connect(a);
  await client.connect(b);
  try { await action(client, statements, () => ({ releases, destroyed })); }
  finally { await client.close(); await server.close(); }
}

test('MCP accepts only its fixed sampler and enforces a read-only transaction with rollback', async () => {
  await withServer(async () => ({ rows: [{ sample: { read_only: 'on' } }] }), async (client, statements, released) => {
    const tools = await client.listTools();
    assert.deepEqual(tools.tools.map(tool => tool.name), ['sample_resources', 'sample_contention']);
    const result = await client.callTool({ name: 'sample_resources', arguments: {} });
    assert.equal(result.isError, undefined);
    assert.equal(statements[0], 'BEGIN READ ONLY');
    assert.deepEqual(statements[1], { text: samplingSql, queryMode: 'extended' });
    assert.equal(statements[2], 'ROLLBACK');
    assert.deepEqual(released(), { releases: 1, destroyed: false });
    for (const request of [{ name: 'query', arguments: { sql: 'DELETE FROM data' } },
      { name: 'sample_resources', arguments: { sql: 'COMMIT; DROP SCHEMA public CASCADE' } }]) {
      await assert.rejects(client.callTool(request));
    }
    assert.equal(statements.length, 3);
  });
});

test('sampling failures release connections, rollback and redact sensitive driver details', async () => {
  await withServer(async value => {
    if (typeof value === 'object') throw Object.assign(new Error('postgres://secret:password@host'), { code: '42501' });
    return {};
  }, async (client, statements, released) => {
    const result = await client.callTool({ name: 'sample_resources', arguments: {} });
    assert.equal(result.isError, true);
    assert.match(result.content[0].text, /42501/);
    assert.doesNotMatch(JSON.stringify(result), /secret|password/);
    assert.equal(statements.at(-1), 'ROLLBACK');
    assert.deepEqual(released(), { releases: 1, destroyed: false });
  });
});

test('contention MCP query is fixed, parameterized and read-only; invalid IDs cannot inject SQL', async () => {
  await withServer(async () => ({ rows: [{ sample: { read_only: 'on' } }] }), async (client, statements, released) => {
    await client.callTool({ name: 'sample_contention', arguments: { jobId: '123' } });
    assert.deepEqual(statements, ['BEGIN READ ONLY', { text: contentionSql, values: ['123'], queryMode: 'extended' }, 'ROLLBACK']);
    assert.equal(released().releases, 1);
    for (const jobId of ['1;DELETE', '-1', '0', '1.2', '9999999999999999999', 123, null]) {
      await assert.rejects(client.callTool({ name: 'sample_contention', arguments: { jobId } }));
    }
    await assert.rejects(client.callTool({ name: 'sample_contention', arguments: { sql: 'SELECT 1' } }));
    assert.equal(statements.length, 3);
    await client.callTool({ name: 'sample_contention', arguments: {} });
    assert.deepEqual(statements[4].values, [null]);
  });
});
