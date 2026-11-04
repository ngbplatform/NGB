import assert from 'node:assert/strict'
import { isAbsolute } from 'node:path'

const environmentNames = new Set(['PATH', 'HOME', 'USERPROFILE', 'TMPDIR', 'CLAUDE_CODE_TMPDIR', 'LANG', 'LC_ALL',
  'DOTNET_ROOT', 'DOTNET_ROOT_ARM64', 'DOTNET_CLI_HOME', 'DOTNET_CLI_TELEMETRY_OPTOUT',
  'DOTNET_NOLOGO', 'NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'npm_config_cache',
  'npm_config_update_notifier', 'CI'])

// This launcher accepts no command or path from the agent. Its caller supplies
// immutable paths and a clean environment before inference. The only operation
// is the target CLI's reviewed upgrade inside a separately enforced OS sandbox.
export function upgradeLauncher({ node, sandboxCli, settings, cli, application, candidate, resolution, environment }) {
  for (const path of [node, sandboxCli, settings, cli, application, candidate, resolution]) {
    assert.ok(typeof path === 'string' && isAbsolute(path) && !/[\r\n\0]/.test(path),
      'Launcher paths must be fixed absolute paths.')
  }
  const quote = value => `'${value.replaceAll("'", "'\\''")}'`
  const variables = Object.entries(environment).map(([name, value]) => {
    assert.ok(environmentNames.has(name) && typeof value === 'string' && !/[\r\n\0]/.test(value),
      'Only explicit non-secret toolchain environment values may enter the launcher.')
    return quote(`${name}=${value}`)
  })
  assert.ok(environment.PATH && environment.HOME && environment.TMPDIR,
    'The launcher requires a pinned PATH and private home/temp directories.')
  const args = [node, sandboxCli, '--settings', settings, '--', node, cli, 'upgrade',
    '--app', application, '--to', '3.4.0', '--packages', candidate,
    '--resolutions', resolution, '--apply', '--format', 'json']
  const continuation = ' ' + '\\' + '\n  '
  return '#!/bin/sh\nset -eu\n'
    + 'if [ "$#" -ne 0 ]; then\n  printf "%s\\n" "This fixed upgrade launcher accepts no arguments." >&2\n  exit 64\nfi\n'
    + 'exec /usr/bin/env -i' + continuation
    + [...variables, ...args.map(quote)].join(continuation) + '\n'
}
