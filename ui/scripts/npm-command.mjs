import { existsSync } from 'node:fs'
import { win32 } from 'node:path'

export function resolveNpmCommand(args, {
  platform = process.platform,
  execPath = process.execPath,
  env = process.env,
  exists = existsSync,
} = {}) {
  if (platform !== 'win32') {
    return { command: 'npm', args }
  }

  // Windows cannot execute npm.cmd through execFileSync without a shell.
  // Use the npm script's CLI, or the CLI bundled with the current Node install.
  const candidates = [
    env.npm_execpath,
    win32.join(win32.dirname(execPath), 'node_modules', 'npm', 'bin', 'npm-cli.js'),
  ]
  const npmCli = candidates.find(path =>
    path && win32.basename(path) === 'npm-cli.js' && exists(path),
  )

  if (!npmCli) {
    throw new Error(
      'Cannot locate npm-cli.js. Run npm --prefix ui run pack:platform-ui -- --local-candidate '
        + 'from the repository root using a Node installation that includes npm.',
    )
  }

  return { command: execPath, args: [npmCli, ...args] }
}
