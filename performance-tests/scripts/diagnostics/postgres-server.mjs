// A narrow Postgres MCP server: one fixed read-only sampling tool, no arbitrary SQL.
import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { CallToolRequestSchema, ListToolsRequestSchema } from '@modelcontextprotocol/sdk/types.js';
import pg from 'pg';

export const samplingSql = readFileSync(new URL('./sql/postgres-resources.sql', import.meta.url), 'utf8');
export const contentionSql = readFileSync(new URL('./sql/contention.sql', import.meta.url), 'utf8');

export function createServer(pool) {
  const server = new Server({ name: 'ngb-postgres-diagnostics', version: '1.0.0' }, { capabilities: { tools: {} } });
  server.setRequestHandler(ListToolsRequestSchema, async () => ({ tools: [{
    name: 'sample_resources', description: 'Read PostgreSQL resource counters and lock activity. PostgreSQL 17+.',
    inputSchema: { type: 'object', properties: {}, additionalProperties: false },
    annotations: { readOnlyHint: true, destructiveHint: false, openWorldHint: false },
  }, {
    name: 'sample_contention', description: 'Read finalization status and one Hangfire job, without job arguments or business data.',
    inputSchema: { type: 'object', properties: { jobId: { type: 'string', pattern: '^[1-9][0-9]{0,17}$' } }, additionalProperties: false },
    annotations: { readOnlyHint: true, destructiveHint: false, openWorldHint: false },
  }] }));
  server.setRequestHandler(CallToolRequestSchema, async request => {
    const args = request.params.arguments || {};
    const contention = request.params.name === 'sample_contention';
    if (contention ? Object.keys(args).some(key => key !== 'jobId') ||
        (args.jobId !== undefined && (typeof args.jobId !== 'string' || !/^[1-9][0-9]{0,17}$/.test(args.jobId)))
      : request.params.name !== 'sample_resources' || Object.keys(args).length) {
      throw new Error('Only the fixed diagnostic sampling tools and their documented arguments are supported.');
    }
    let connection;
    try {
      connection = await pool.connect();
      await connection.query('BEGIN READ ONLY');
      const result = await connection.query(contention
        ? { text: contentionSql, values: [args.jobId ?? null], queryMode: 'extended' }
        : { text: samplingSql, queryMode: 'extended' });
      return { content: [{ type: 'text', text: JSON.stringify(result.rows) }] };
    } catch (error) {
      // Do not forward driver messages, SQL or credentials to artifacts/MCP clients.
      const code = /^[A-Z0-9]{5}$/.test(error.code || '') ? error.code : 'unavailable';
      return { isError: true, content: [{ type: 'text', text: `Postgres resource sampling failed (${code}).` }] };
    } finally {
      if (connection) {
        let failedRollback = false;
        try { await connection.query('ROLLBACK'); } catch { failedRollback = true; }
        connection.release(failedRollback);
      }
    }
  });
  return server;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const pool = new pg.Pool({ max: 1, connectionTimeoutMillis: 5000, query_timeout: 5000,
    application_name: 'ngb.perf.postgres-mcp',
    options: '-c default_transaction_read_only=on -c statement_timeout=3000 -c lock_timeout=1000',
  });
  pool.on('error', () => {}); // The next sampling request reports an unavailable connection.
  const server = createServer(pool);
  let closing;
  const close = () => closing ||= (async () => { await server.close(); await pool.end(); })();
  process.on('SIGTERM', () => { void close(); });
  process.on('SIGINT', () => { void close(); });
  process.stdin.on('end', () => { void close(); });
  await server.connect(new StdioServerTransport());
}
