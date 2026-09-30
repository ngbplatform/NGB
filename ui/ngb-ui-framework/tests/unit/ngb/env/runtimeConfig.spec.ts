import { afterEach, describe, expect, it } from 'vitest'

import { readAppEnv } from '../../../../src/ngb/env/runtimeConfig'

describe('runtime config', () => {
  const originalWindow = globalThis.window

  afterEach(() => {
    globalThis.window = originalWindow as typeof window
  })

  it('falls back to build-time environment when no browser window exists', () => {
    globalThis.window = undefined as never

    expect(readAppEnv('NGB_UNKNOWN_RUNTIME_VALUE')).toBe('')
  })

  it('prefers own runtime values and normalizes null values', () => {
    globalThis.window = {
      __NGB_RUNTIME_CONFIG__: {
        API_URL: '  https://api.ngb.test  ',
        FEATURE_ENABLED: true,
        EMPTY_VALUE: null,
      },
    } as typeof window

    expect(readAppEnv('API_URL')).toBe('https://api.ngb.test')
    expect(readAppEnv('FEATURE_ENABLED')).toBe('true')
    expect(readAppEnv('EMPTY_VALUE')).toBe('')
    expect(readAppEnv('toString')).toBe('')
    expect(readAppEnv('MODE')).not.toBe('')
  })

  it('ignores null and non-object runtime containers', () => {
    globalThis.window = { __NGB_RUNTIME_CONFIG__: null as never } as typeof window
    expect(readAppEnv('NGB_UNKNOWN_RUNTIME_VALUE')).toBe('')

    globalThis.window = { __NGB_RUNTIME_CONFIG__: 'invalid' as never } as typeof window
    expect(readAppEnv('NGB_UNKNOWN_RUNTIME_VALUE')).toBe('')
  })
})
