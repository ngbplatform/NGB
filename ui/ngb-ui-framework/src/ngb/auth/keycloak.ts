import Keycloak, {
  type KeycloakInitOptions,
  type KeycloakLoginOptions,
  type KeycloakLogoutOptions,
  type KeycloakTokenParsed,
} from 'keycloak-js'
import type { AuthSnapshot } from './types'
import { readAppEnv } from '../env/runtimeConfig'

type KeycloakClaims = KeycloakTokenParsed & {
  email?: string
  preferred_username?: string
  given_name?: string
  family_name?: string
  name?: string
  role?: string | string[]
  roles?: string | string[]
  groups?: string[]
}

type AuthListener = (snapshot: AuthSnapshot) => void

const keycloak = new Keycloak({
  url: requiredEnv('VITE_KEYCLOAK_URL'),
  realm: requiredEnv('VITE_KEYCLOAK_REALM'),
  clientId: requiredEnv('VITE_KEYCLOAK_CLIENT_ID'),
})

const listeners = new Set<AuthListener>()
let initialized = false
let initPromise: Promise<boolean> | null = null
let refreshPromise: Promise<string | null> | null = null
let callbacksRegistered = false

function requiredEnv(name: string): string {
  const value = readAppEnv(name)
  if (!value) throw new Error(`Missing required env var: ${name}`)
  return value
}

function normalizeUrl(value: string | null | undefined, fallbackPath: string): string {
  const raw = String(value ?? '').trim()
  if (!raw) return new URL(fallbackPath, window.location.origin).toString()

  const next = new URL(raw, window.location.origin)
  if (next.host === window.location.host && next.protocol !== window.location.protocol) {
    next.protocol = window.location.protocol
  }

  return next.toString()
}

function resolveAppBaseUrl(): string {
  const rawBase = String(import.meta.env.BASE_URL ?? '/').trim()
  const normalizedBase = rawBase
    ? (rawBase.endsWith('/') ? rawBase : `${rawBase}/`)
    : '/'

  return new URL(normalizedBase, window.location.origin).toString()
}

function resolveAppPublicUrl(path: string): string {
  const normalizedPath = String(path ?? '').trim().replace(/^\/+/, '')
  return new URL(normalizedPath, resolveAppBaseUrl()).toString()
}

function parseBoolean(value: string | boolean | null | undefined, defaultValue: boolean): boolean {
  if (typeof value === 'boolean') return value

  const normalized = String(value ?? '').trim().toLowerCase()
  if (!normalized) return defaultValue
  if (normalized === 'true' || normalized === '1' || normalized === 'yes' || normalized === 'on') return true
  if (normalized === 'false' || normalized === '0' || normalized === 'no' || normalized === 'off') return false
  return defaultValue
}

function resolveOnLoad(): 'login-required' | 'check-sso' {
  const normalized = (readAppEnv('VITE_KEYCLOAK_ON_LOAD') || 'login-required').toLowerCase()
  return normalized === 'check-sso' ? 'check-sso' : 'login-required'
}

function shouldUseSilentCheckSso(onLoad: 'login-required' | 'check-sso'): boolean {
  if (onLoad !== 'check-sso') return false
  return parseBoolean(readAppEnv('VITE_KEYCLOAK_SILENT_CHECK_SSO_ENABLED'), false)
}

function resolveSilentCheckSsoRedirectUri(): string {
  return normalizeUrl(readAppEnv('VITE_KEYCLOAK_SILENT_CHECK_SSO_REDIRECT_URI'), resolveAppPublicUrl('silent-check-sso.html'))
}

function resolvePostLogoutRedirectUri(): string {
  const explicit = readAppEnv('VITE_KEYCLOAK_POST_LOGOUT_REDIRECT_URL')
  if (explicit) return normalizeUrl(explicit, resolveAppBaseUrl())
  return normalizeUrl(readAppEnv('VITE_KEYCLOAK_REDIRECT_URL'), resolveAppBaseUrl())
}

function resolveAdminConsoleLocalLogoutEndpoints(): string[] {
  const configuredUrls = [
    readAppEnv('VITE_WATCHDOG_URL'),
    readAppEnv('VITE_BACKGROUND_JOB_URL'),
  ]

  return Array.from(
    new Set(
      configuredUrls
        .map((value) => String(value ?? '').trim())
        .filter((value) => value.length > 0)
        .map((value) => new URL(value, window.location.origin))
        .map((url) => `${url.origin}/account/local-logout`),
    ),
  )
}

async function logoutKnownAdminConsoles(): Promise<void> {
  const endpoints = resolveAdminConsoleLocalLogoutEndpoints()
  if (endpoints.length === 0) return

  await Promise.allSettled(
    endpoints.map(async (endpoint) => {
      await fetch(endpoint, {
        method: 'POST',
        credentials: 'include',
        mode: 'cors',
        cache: 'no-store',
        keepalive: true,
        headers: {
          Accept: '*/*',
        },
      })
    }),
  )
}

function resolveLoginRedirectUri(targetPath?: string | null): string {
  const target = String(targetPath ?? '').trim()
  if (/^https?:\/\//i.test(target)) return target
  if (target.startsWith('/')) return new URL(target, window.location.origin).toString()

  const currentUrl = new URL(window.location.href)
  currentUrl.hash = ''
  return currentUrl.toString()
}

function unique(values: Iterable<string | null | undefined>): string[] {
  return Array.from(
    new Set(
      Array.from(values)
        .map((value) => String(value ?? '').trim())
        .filter((value) => value.length > 0),
    ),
  )
}

function readStringArrayClaim(value: unknown): string[] {
  if (Array.isArray(value)) return unique(value.map((entry) => String(entry ?? '')))
  if (typeof value === 'string') return unique(value.split(/[,\s]+/g))
  return []
}

function accessClaims(): KeycloakClaims | null {
  return (keycloak.tokenParsed ?? null) as KeycloakClaims | null
}

function identityClaims(): KeycloakClaims | null {
  const access = accessClaims()
  const identity = (keycloak.idTokenParsed ?? null) as KeycloakClaims | null
  if (!access && !identity) return null

  return {
    ...(access ?? {}),
    ...(identity ?? {}),
  }
}

function readRealmRoles(claims: KeycloakClaims | null): string[] {
  return unique(claims?.realm_access?.roles ?? [])
}

function readTopLevelRoles(claims: KeycloakClaims | null): string[] {
  return unique([
    ...readStringArrayClaim(claims?.role),
    ...readStringArrayClaim(claims?.roles),
  ])
}

function readResourceRoles(claims: KeycloakClaims | null): Record<string, string[]> {
  const resourceAccess = claims?.resource_access ?? {}
  const entries = Object.entries(resourceAccess).map(([clientId, access]) => [
    clientId,
    unique(access?.roles ?? []),
  ] as const)

  return Object.fromEntries(entries.filter(([, roles]) => roles.length > 0))
}

function readGroupRoles(claims: KeycloakClaims | null): string[] {
  return unique(
    readStringArrayClaim(claims?.groups)
      .map((group) => group.split('/').filter(Boolean).at(-1) ?? '')
      .filter((group) => group === 'ngb-admin' || group === 'ngb-user'),
  )
}

function resolveDisplayName(claims: KeycloakClaims | null): string | null {
  const explicitName = String(claims?.name ?? '').trim()
  if (explicitName) return explicitName

  const fullName = [claims?.given_name, claims?.family_name]
    .map((part) => String(part ?? '').trim())
    .filter((part) => part.length > 0)
    .join(' ')

  if (fullName) return fullName

  const preferredUsername = String(claims?.preferred_username ?? '').trim()
  if (preferredUsername) return preferredUsername

  const email = String(claims?.email ?? '').trim()
  return email || null
}

function snapshot(): AuthSnapshot {
  const profile = identityClaims()
  const access = accessClaims()
  const topLevelRoles = readTopLevelRoles(access)
  const realmRoles = readRealmRoles(access)
  const resourceRoles = readResourceRoles(access)
  const groupRoles = readGroupRoles(profile)
  const roles = unique([
    ...topLevelRoles,
    ...realmRoles,
    ...Object.values(resourceRoles).flat(),
    ...groupRoles,
  ])

  return {
    initialized,
    authenticated: Boolean(keycloak.authenticated),
    token: keycloak.token ?? null,
    subject: keycloak.subject ?? (typeof profile?.sub === 'string' ? profile.sub : null),
    displayName: resolveDisplayName(profile),
    preferredUsername: String(profile?.preferred_username ?? '').trim() || null,
    email: String(profile?.email ?? '').trim() || null,
    realmRoles,
    resourceRoles,
    roles,
  }
}

function notifyListeners(): void {
  const nextSnapshot = snapshot()
  for (const listener of listeners) listener(nextSnapshot)
}

function registerCallbacks(): void {
  if (callbacksRegistered) return
  callbacksRegistered = true

  keycloak.onReady = () => {
    initialized = true
    notifyListeners()
  }
  keycloak.onAuthSuccess = notifyListeners
  keycloak.onAuthLogout = notifyListeners
  keycloak.onAuthRefreshSuccess = notifyListeners
  keycloak.onAuthRefreshError = notifyListeners
  keycloak.onAuthError = notifyListeners
  keycloak.onTokenExpired = () => {
    void refreshAccessToken(0).catch(() => {
      keycloak.clearToken()
      notifyListeners()
    })
  }
}

export function subscribeAuth(listener: AuthListener): () => void {
  listeners.add(listener)
  listener(snapshot())
  return () => {
    listeners.delete(listener)
  }
}

export function getAuthSnapshot(): AuthSnapshot {
  return snapshot()
}

export async function initializeAuth(): Promise<AuthSnapshot> {
  registerCallbacks()

  if (!initPromise) {
    const onLoad = resolveOnLoad()
    const initOptions: KeycloakInitOptions = {
      onLoad,
      pkceMethod: 'S256',
      responseMode: 'fragment',
      checkLoginIframe: false,
    }

    if (shouldUseSilentCheckSso(onLoad)) {
      initOptions.silentCheckSsoRedirectUri = resolveSilentCheckSsoRedirectUri()
      initOptions.silentCheckSsoFallback = false
    }

    initPromise = keycloak
      .init(initOptions)
      .then((authenticated) => {
        initialized = true
        notifyListeners()
        return authenticated
      })
      .catch((error) => {
        initPromise = null
        throw error
      })
  }

  await initPromise
  return snapshot()
}

async function refreshAccessToken(minValidity: number): Promise<string | null> {
  if (!initialized) await initializeAuth()
  if (!keycloak.authenticated || !keycloak.token) return null

  if (!refreshPromise) {
    refreshPromise = keycloak
      .updateToken(minValidity)
      .then(() => {
        notifyListeners()
        return keycloak.token ?? null
      })
      .catch((error) => {
        keycloak.clearToken()
        notifyListeners()
        throw error
      })
      .finally(() => {
        refreshPromise = null
      })
  }

  return await refreshPromise
}

export async function getAccessToken(minValidity: number = 30): Promise<string | null> {
  return await refreshAccessToken(minValidity)
}

export async function forceRefreshAccessToken(): Promise<string | null> {
  return await refreshAccessToken(-1)
}

export async function loginWithKeycloak(targetPath?: string | null): Promise<void> {
  if (!initialized) await initializeAuth()

  const options: KeycloakLoginOptions = {
    redirectUri: resolveLoginRedirectUri(targetPath),
  }

  await keycloak.login(options)
}

export async function logoutFromKeycloak(): Promise<void> {
  if (!initialized) await initializeAuth()

  await logoutKnownAdminConsoles()

  const options: KeycloakLogoutOptions = {
    redirectUri: resolvePostLogoutRedirectUri(),
  }

  await keycloak.logout(options)
}
