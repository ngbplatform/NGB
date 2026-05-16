import fs from 'node:fs'
import path from 'node:path'

type EnvMap = Record<string, string>
type EnvSource = Record<string, string | undefined>

type E2eEnvOptions = {
  rootDir?: string
  appDirectory: string
  envFiles?: readonly string[]
}

const DEFAULT_E2E_ENV_FILES = ['.env.e2e.local'] as const

function stripWrappingQuotes(value: string): string {
  if ((value.startsWith('"') && value.endsWith('"')) || (value.startsWith("'") && value.endsWith("'"))) {
    return value.slice(1, -1)
  }

  return value
}

function parseEnvFile(contents: string): EnvMap {
  const entries: EnvMap = {}

  for (const rawLine of contents.split(/\r?\n/g)) {
    const line = rawLine.trim()
    if (!line || line.startsWith('#')) continue

    const separatorIndex = line.indexOf('=')
    if (separatorIndex <= 0) continue

    const key = line.slice(0, separatorIndex).trim()
    const value = stripWrappingQuotes(line.slice(separatorIndex + 1).trim())
    if (!key) continue
    entries[key] = value
  }

  return entries
}

function readEnvFile(filePath: string): EnvMap {
  if (!fs.existsSync(filePath)) return {}
  return parseEnvFile(fs.readFileSync(filePath, 'utf8'))
}

function resolveUiWorkspaceDir(rootDir: string): string {
  if (fs.existsSync(path.join(rootDir, 'package.json'))) return rootDir

  const nestedUiDir = path.join(rootDir, 'ui')
  if (fs.existsSync(path.join(nestedUiDir, 'package.json'))) return nestedUiDir

  return rootDir
}

export function loadE2eEnv(options: E2eEnvOptions): EnvMap {
  const rootDir = options.rootDir ?? process.cwd()
  const uiWorkspaceDir = resolveUiWorkspaceDir(rootDir)
  const appDir = path.join(uiWorkspaceDir, options.appDirectory)
  const envFiles = options.envFiles ?? DEFAULT_E2E_ENV_FILES
  const loaded: EnvMap = {}

  for (const fileName of envFiles) {
    const filePath = path.join(appDir, fileName)
    Object.assign(loaded, readEnvFile(filePath))
  }

  for (const [key, value] of Object.entries(loaded)) {
    if (!(key in process.env)) process.env[key] = value
  }

  return {
    ...loaded,
    ...Object.fromEntries(
      Object.entries(process.env)
        .filter((entry): entry is [string, string] => typeof entry[1] === 'string'),
    ),
  }
}

export function requireE2eEnv(env: EnvSource, name: string): string {
  const value = String(env[name] ?? '').trim()
  if (!value) throw new Error(`Missing required e2e env var: ${name}`)
  return value
}

export function resolvePlaywrightAuthFile(
  rootDir: string = process.cwd(),
  browserName?: string,
): string {
  const uiWorkspaceDir = resolveUiWorkspaceDir(rootDir)
  const suffix = String(browserName ?? '').trim()
  const fileName = suffix ? `e2e-user-${suffix}.json` : 'e2e-user.json'
  return path.join(uiWorkspaceDir, 'playwright', '.auth', fileName)
}
