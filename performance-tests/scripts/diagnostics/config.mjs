import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
import { parseEnv, parseArgs } from 'node:util';
import { contentionConfig } from './contention-config.mjs';

export const workspace = fileURLToPath(new URL('../../', import.meta.url));
export const profiles = ['write-heavy', 'platform-mixed-capacity', 'platform-read-capacity', 'platform-breakpoint', 'platform-contention'];

export function loadEnvironment(path, inherited = process.env) {
  return { ...parseEnv(readFileSync(path, 'utf8')), ...inherited };
}

export function parseOptions(args) {
  const { values } = parseArgs({ args, options: {
    profile: { type: 'string' }, 'env-file': { type: 'string' },
    'database-env-file': { type: 'string', default: '../.env.pm' },
    'dataset-id': { type: 'string' }, check: { type: 'boolean', default: false },
    help: { type: 'boolean', default: false },
  } });
  if (!values.help && !profiles.includes(values.profile)) {
    throw new Error(`--profile must be one of: ${profiles.join(', ')}`);
  }
  return values;
}

export function workloadEnvironment(profile, env) {
  const writes = ['write-heavy', 'platform-mixed-capacity', 'platform-contention'].includes(profile);
  const result = { ...env,
    NGB_PERF_ENABLE_WRITES: String(writes),
    NGB_PERF_ENABLE_POSTING: String(writes),
    NGB_PERF_ENABLE_PERIOD_CLOSE: 'false',
    NGB_PM_POSTING_MODE: 'fresh',
  };
  if (profile === 'platform-contention') {
    const config = contentionConfig(env);
    Object.assign(result, { NGB_CAPACITY_VUS: String(config.vus), NGB_CAPACITY_RAMP_DURATION: config.ramp,
      NGB_CAPACITY_HOLD_DURATION: config.hold, NGB_CAPACITY_RAMP_DOWN_DURATION: config.down });
  }
  if (profile.endsWith('-capacity')) {
    result.NGB_CAPACITY_VUS ||= '80,160,240,320';
    result.NGB_CAPACITY_RAMP_DURATION ||= '5m';
    result.NGB_CAPACITY_HOLD_DURATION ||= '10m';
    result.NGB_CAPACITY_RAMP_DOWN_DURATION ||= '5m';
    if (!/^\d+(,\d+)*$/.test(result.NGB_CAPACITY_VUS)
        || result.NGB_CAPACITY_VUS.split(',').some(value => Number(value) <= 0)) {
      throw new Error('NGB_CAPACITY_VUS must contain comma-separated positive integers.');
    }
    for (const key of ['NGB_CAPACITY_RAMP_DURATION', 'NGB_CAPACITY_HOLD_DURATION', 'NGB_CAPACITY_RAMP_DOWN_DURATION']) {
      if (!/^(?:\d+(?:\.\d+)?(?:ms|s|m|h))+$/.test(result[key])) {
        throw new Error(`${key} must be a k6 duration such as 30s, 5m or 1h30m.`);
      }
    }
  }
  return result;
}

export function databaseEnvironment(file, inherited = process.env) {
  const env = loadEnvironment(resolve(workspace, file), inherited);
  const result = {
    PATH: inherited.PATH || '',
    PGHOST: env.PGHOST || 'localhost',
    PGPORT: env.PGPORT || env.POSTGRES_HOST_PORT || '5432',
    PGDATABASE: env.PGDATABASE || env.PM_DB_NAME,
    PGUSER: env.PGUSER || env.POSTGRES_ADMIN_USER,
    PGPASSWORD: env.PGPASSWORD || env.POSTGRES_ADMIN_PASSWORD,
    PGSSLMODE: env.PGSSLMODE || 'disable',
  };
  for (const key of ['PGDATABASE', 'PGUSER', 'PGPASSWORD']) {
    if (!result[key]) throw new Error(`Database configuration is missing ${key}.`);
  }
  if (!/^\d+$/.test(result.PGPORT) || Number(result.PGPORT) < 1 || Number(result.PGPORT) > 65535) {
    throw new Error('PGPORT must be between 1 and 65535.');
  }
  return result;
}
