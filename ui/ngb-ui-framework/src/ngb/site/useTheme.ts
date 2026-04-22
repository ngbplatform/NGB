import { computed, onMounted, ref, watch } from 'vue'

import { readCookie, readStorageString, writeCookie, writeStorageString } from '../utils/storage'

export type ThemeMode = 'light' | 'dark' | 'system'
const KEY = 'ngb.theme'
const COOKIE_MAX_AGE_SECONDS = 60 * 60 * 24 * 400

function prefersDark() {
  return typeof window !== 'undefined'
    && window.matchMedia
    && window.matchMedia('(prefers-color-scheme: dark)').matches
}

function readThemeCookie(): ThemeMode | null {
  const value = readCookie(KEY)
  return value === 'light' || value === 'dark' || value === 'system'
    ? value
    : null
}

function isIpAddress(hostname: string) {
  return /^(?:\d{1,3}\.){3}\d{1,3}$/.test(hostname)
    || hostname.includes(':')
}

function resolveSharedCookieDomain(hostname: string) {
  const normalized = hostname.trim().toLowerCase()

  if (!normalized || normalized === 'localhost' || isIpAddress(normalized) || !normalized.includes('.'))
    return null

  return normalized
}

function persistThemeCookie(mode: ThemeMode) {
  if (typeof document === 'undefined' || typeof window === 'undefined')
    return

  const secure = window.location.protocol === 'https:'
  writeCookie(KEY, mode, {
    path: '/',
    maxAgeSeconds: COOKIE_MAX_AGE_SECONDS,
    sameSite: 'Lax',
    secure,
  })

  const sharedDomain = resolveSharedCookieDomain(window.location.hostname)
  if (sharedDomain)
    writeCookie(KEY, mode, {
      path: '/',
      maxAgeSeconds: COOKIE_MAX_AGE_SECONDS,
      sameSite: 'Lax',
      secure,
      domain: sharedDomain,
    })
}

export function useTheme() {
  const mode = ref<ThemeMode>('system')

  onMounted(() => {
    const saved = readStorageString('local', KEY)
    if (saved === 'light' || saved === 'dark' || saved === 'system') {
      mode.value = saved
      return
    }

    const cookie = readThemeCookie()
    if (cookie) mode.value = cookie
  })

  const resolved = computed<'light' | 'dark'>(() => {
    if (mode.value === 'system') return prefersDark() ? 'dark' : 'light'
    return mode.value
  })

  function apply() {
    const el = document.documentElement
    if (resolved.value === 'dark') el.classList.add('dark')
    else el.classList.remove('dark')
  }

  watch([mode, resolved], () => {
    void writeStorageString('local', KEY, mode.value)
    persistThemeCookie(mode.value)
    apply()
  }, { immediate: true })

  function toggle() {
    mode.value = resolved.value === 'dark' ? 'light' : 'dark'
  }

  return { mode, resolved, toggle }
}
