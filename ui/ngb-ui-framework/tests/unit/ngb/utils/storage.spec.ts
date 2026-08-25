import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  canUseStorage,
  listStorageKeys,
  loadJson,
  readCookie,
  readStorageJson,
  readStorageJsonOrNull,
  readStorageString,
  removeStorageItem,
  saveJson,
  writeCookie,
  writeStorageJson,
  writeStorageString,
} from '../../../../src/ngb/utils/storage'

function createStorageMock(behavior: 'normal' | 'throwing' = 'normal') {
  const storage: Record<string, unknown> = {}

  Object.defineProperties(storage, {
    getItem: {
      enumerable: false,
      value(key: string) {
        if (behavior === 'throwing') throw new Error('boom')
        return typeof storage[key] === 'string' ? storage[key] : null
      },
    },
    setItem: {
      enumerable: false,
      value(key: string, value: string) {
        if (behavior === 'throwing') throw new Error('boom')
        storage[key] = value
      },
    },
    removeItem: {
      enumerable: false,
      value(key: string) {
        if (behavior === 'throwing') throw new Error('boom')
        delete storage[key]
      },
    },
  })

  return storage as Storage
}

describe('storage helpers', () => {
  beforeEach(() => {
    vi.stubGlobal('window', {
      localStorage: createStorageMock(),
      sessionStorage: createStorageMock(),
    })
    vi.stubGlobal('document', {
      cookie: '',
    })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('reads, writes, lists, and removes local/session storage values', () => {
    expect(canUseStorage('local')).toBe(true)
    expect(writeStorageString('local', 'alpha', 'one')).toBe(true)
    expect(writeStorageString('session', 'beta', 'two')).toBe(true)
    expect(readStorageString('local', 'alpha')).toBe('one')
    expect(readStorageString('session', 'beta')).toBe('two')
    expect(listStorageKeys('local')).toEqual(['alpha'])

    removeStorageItem('local', 'alpha')
    expect(readStorageString('local', 'alpha')).toBeNull()
  })

  it('reads and writes json payloads with safe fallbacks', () => {
    expect(writeStorageJson('local', 'payload', { value: 42 })).toBe(true)
    expect(readStorageJson('local', 'payload', { value: 0 })).toEqual({ value: 42 })
    expect(readStorageJsonOrNull('local', 'payload')).toEqual({ value: 42 })

    writeStorageString('local', 'invalid', '{bad json')
    expect(readStorageJson('local', 'invalid', { safe: true })).toEqual({ safe: true })
    expect(readStorageJsonOrNull('local', 'invalid')).toBeNull()

    saveJson('saved', { ok: true })
    expect(loadJson('saved', { ok: false })).toEqual({ ok: true })
  })

  it('returns safe defaults when storage access throws and encodes cookie values', () => {
    vi.stubGlobal('window', {
      localStorage: createStorageMock('throwing'),
      sessionStorage: createStorageMock('throwing'),
    })

    expect(canUseStorage('local')).toBe(true)
    expect(writeStorageString('local', 'alpha', 'one')).toBe(false)
    expect(readStorageString('local', 'alpha')).toBeNull()
    expect(listStorageKeys('local')).toEqual([])
    expect(() => removeStorageItem('local', 'alpha')).not.toThrow()

    writeCookie('auth_token', 'a value', {
      path: '/portal',
      maxAgeSeconds: 61.9,
      sameSite: 'Strict',
      secure: true,
      domain: 'example.test',
    })
    expect(document.cookie).toContain('auth_token=a%20value')
    expect(document.cookie).toContain('Path=/portal')
    expect(document.cookie).toContain('Max-Age=61')
    expect(document.cookie).toContain('SameSite=Strict')
    expect(document.cookie).toContain('Secure')
    expect(document.cookie).toContain('Domain=example.test')

    ;(document as { cookie: string }).cookie = 'theme=light; auth_token=a%20value'
    expect(readCookie('auth_token')).toBe('a value')
    expect(readCookie('missing')).toBeNull()
  })

  it('returns safe defaults without browser globals or usable storage properties', () => {
    vi.stubGlobal('window', undefined)
    vi.stubGlobal('document', undefined)

    expect(canUseStorage('local')).toBe(false)
    expect(readStorageString('local', 'missing')).toBeNull()
    expect(writeStorageString('local', 'key', 'value')).toBe(false)
    expect(() => removeStorageItem('local', 'key')).not.toThrow()
    expect(listStorageKeys('local')).toEqual([])
    expect(readStorageJson('local', 'missing', { fallback: true })).toEqual({ fallback: true })
    expect(readStorageJsonOrNull('local', 'missing')).toBeNull()
    expect(readCookie('missing')).toBeNull()
    expect(() => writeCookie('ignored', 'value')).not.toThrow()

    const circular: { self?: unknown } = {}
    circular.self = circular
    expect(writeStorageJson('local', 'circular', circular)).toBe(false)

    vi.stubGlobal('window', Object.defineProperties({}, {
      localStorage: { get: () => { throw new Error('blocked') } },
      sessionStorage: { get: () => { throw new Error('blocked') } },
    }))
    expect(canUseStorage('local')).toBe(false)
    expect(canUseStorage('session')).toBe(false)

    vi.stubGlobal('window', {
      localStorage: new Proxy(createStorageMock(), {
        ownKeys: () => { throw new Error('cannot enumerate') },
      }),
      sessionStorage: createStorageMock(),
    })
    expect(listStorageKeys('local')).toEqual([])
  })

  it('writes default cookie attributes and clamps negative max age', () => {
    writeCookie('theme', 'dark')
    expect(document.cookie).toBe('theme=dark; Path=/; SameSite=Lax')

    writeCookie('expired', 'yes', { maxAgeSeconds: -3.2 })
    expect(document.cookie).toContain('Max-Age=0')
    expect(document.cookie).not.toContain('Secure')
    expect(document.cookie).not.toContain('Domain=')
  })
})
