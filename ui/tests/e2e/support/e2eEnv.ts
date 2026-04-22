import fs from 'node:fs'
import path from 'node:path'

type EnvMap = Record<string, string>

const PLAYWRIGHT_ENV_FILES = ['.env.e2e.local'] as const

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

export function loadPmWebE2eEnv(rootDir: string = process.cwd()): EnvMap {
  const appDir = path.join(rootDir, 'ngb-property-management-web')
  const loaded: EnvMap = {}

  for (const fileName of PLAYWRIGHT_ENV_FILES) {
    const filePath = path.join(appDir, fileName)
    if (!fs.existsSync(filePath)) continue

    const parsed = parseEnvFile(fs.readFileSync(filePath, 'utf8'))
    Object.assign(loaded, parsed)
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

export function requireE2eEnv(env: EnvMap, name: string): string {
  const value = String(env[name] ?? '').trim()
  if (!value) throw new Error(`Missing required e2e env var: ${name}`)
  return value
}

export function resolvePlaywrightAuthFile(rootDir: string = process.cwd()): string {
  return path.join(rootDir, 'playwright', '.auth', 'ngb-tester.json')
}
