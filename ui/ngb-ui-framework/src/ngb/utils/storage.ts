export type StorageScope = 'local' | 'session'

export type CookieOptions = {
  path?: string
  maxAgeSeconds?: number
  sameSite?: 'Lax' | 'Strict' | 'None'
  secure?: boolean
  domain?: string | null
}

function resolveBrowserStorage(scope: StorageScope): Storage | null {
  if (typeof window === 'undefined') return null

  try {
    return scope === 'session' ? window.sessionStorage : window.localStorage
  } catch {
    return null
  }
}

export function canUseStorage(scope: StorageScope): boolean {
  return resolveBrowserStorage(scope) !== null
}

export function readStorageString(scope: StorageScope, key: string): string | null {
  const storage = resolveBrowserStorage(scope)
  if (!storage) return null

  try {
    return storage.getItem(key)
  } catch {
    return null
  }
}

export function writeStorageString(scope: StorageScope, key: string, value: string): boolean {
  const storage = resolveBrowserStorage(scope)
  if (!storage) return false

  try {
    storage.setItem(key, value)
    return true
  } catch {
    return false
  }
}

export function removeStorageItem(scope: StorageScope, key: string): void {
  const storage = resolveBrowserStorage(scope)
  if (!storage) return

  try {
    storage.removeItem(key)
  } catch {
    // Ignore storage cleanup failures.
  }
}

export function listStorageKeys(scope: StorageScope): string[] {
  const storage = resolveBrowserStorage(scope)
  if (!storage) return []

  try {
    return Object.keys(storage)
  } catch {
    return []
  }
}

export function readStorageJson<T>(scope: StorageScope, key: string, fallback: T): T {
  const raw = readStorageString(scope, key)
  if (!raw) return fallback

  try {
    return JSON.parse(raw) as T
  } catch {
    return fallback
  }
}

export function readStorageJsonOrNull<T>(scope: StorageScope, key: string): T | null {
  const raw = readStorageString(scope, key)
  if (!raw) return null

  try {
    return JSON.parse(raw) as T
  } catch {
    return null
  }
}

export function writeStorageJson(scope: StorageScope, key: string, value: unknown): boolean {
  try {
    return writeStorageString(scope, key, JSON.stringify(value))
  } catch {
    return false
  }
}

export function loadJson<T>(key: string, fallback: T): T {
  return readStorageJson('local', key, fallback)
}

export function saveJson(key: string, value: unknown): void {
  void writeStorageJson('local', key, value)
}

export function readCookie(name: string): string | null {
  if (typeof document === 'undefined') return null

  const prefix = `${name}=`
  const cookie = document.cookie
    .split(';')
    .map((part) => part.trim())
    .find((part) => part.startsWith(prefix))

  if (!cookie) return null
  return decodeURIComponent(cookie.slice(prefix.length))
}

export function writeCookie(name: string, value: string, options: CookieOptions = {}): void {
  if (typeof document === 'undefined') return

  const parts = [
    `${name}=${encodeURIComponent(value)}`,
    `Path=${options.path ?? '/'}`,
    `SameSite=${options.sameSite ?? 'Lax'}`,
  ]

  if (Number.isFinite(options.maxAgeSeconds)) parts.push(`Max-Age=${Math.max(0, Math.floor(options.maxAgeSeconds ?? 0))}`)
  if (options.secure) parts.push('Secure')
  if (options.domain) parts.push(`Domain=${options.domain}`)

  document.cookie = parts.join('; ')
}
