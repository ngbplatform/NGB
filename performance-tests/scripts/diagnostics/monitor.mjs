import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';

const execute = promisify(execFile);
export async function command(name, args, options = {}) {
  const { stdout } = await execute(name, args, { timeout: 10000, maxBuffer: 4 * 1024 * 1024, ...options });
  return stdout.trim();
}

export class Monitor {
  constructor(databaseEnv, containers) {
    this.databaseEnv = databaseEnv;
    this.containers = containers;
    this.client = new Client({ name: 'ngb-performance-diagnostics', version: '1.0.0' }, { capabilities: {} });
  }

  async connect() {
    this.transport = new StdioClientTransport({ command: process.execPath,
      args: [fileURLToPath(new URL('./postgres-server.mjs', import.meta.url))],
      env: this.databaseEnv, stderr: 'pipe',
    });
    await this.client.connect(this.transport, { timeout: 10000 });
    this.transport.stderr?.resume();
  }

  async sample(includeContainers = true) {
    const sample = { at: new Date().toISOString() };
    const collect = async (key, read) => {
      try { sample[key] = await read(); }
      catch { sample[key] = { error: `${key} sampling failed` }; }
    };
    await Promise.all([
      collect('postgres_mcp', async () => {
        const result = await this.client.callTool({ name: 'sample_resources', arguments: {} }, undefined, { timeout: 8000 });
        if (result.isError) throw new Error('Postgres MCP returned an error.');
        const rows = JSON.parse(result.content.find(item => item.type === 'text').text);
        if (rows[0]?.sample?.read_only !== 'on') throw new Error('Expected a read-only transaction.');
        return { server: this.client.getServerVersion(), rows };
      }),
      ...(includeContainers ? [collect('containers', async () =>
        (await command('docker', ['stats', '--no-stream', '--format', '{{json .}}', ...this.containers]))
          .split('\n').filter(Boolean).map(line => JSON.parse(line)))] : []),
      collect('load_generator', async () => {
        const lines = (await command('ps', ['-axo', 'pid=,pcpu=,rss=,comm='])).split('\n');
        return lines.flatMap(line => {
          const match = line.trim().match(/^(\d+)\s+([\d.]+)\s+(\d+)\s+(.*)$/);
          return match && match[4].split('/').at(-1) === 'k6'
            ? [{ pid: Number(match[1]), cpuPercent: Number(match[2]), rssKiB: Number(match[3]) }] : [];
        });
      }),
    ]);
    return sample;
  }

  async close() { await this.client.close(); }
}

export async function inspectContainers(names) {
  return Promise.all(names.map(async name => {
    // Select only non-secret fields. Never serialize a complete docker inspect response.
    const fields = JSON.parse(await command('docker', ['inspect', '--format',
      '{"id":{{json .Id}},"image":{{json .Image}},"running":{{json .State.Running}},"health":{{if index .State "Health"}}{{json .State.Health.Status}}{{else}}null{{end}},"memoryBytes":{{json .HostConfig.Memory}},"nanoCpus":{{json .HostConfig.NanoCpus}},"cpuQuota":{{json .HostConfig.CpuQuota}},"cpuPeriod":{{json .HostConfig.CpuPeriod}},"cpuSet":{{json .HostConfig.CpusetCpus}}}', name]));
    if (!fields.running || (fields.health && fields.health !== 'healthy')) {
      throw new Error(`Container ${name} must be running and healthy before a diagnostic run.`);
    }
    return { name, ...fields };
  }));
}
