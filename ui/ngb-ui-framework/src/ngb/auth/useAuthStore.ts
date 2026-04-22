import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import {
  getAuthSnapshot,
  initializeAuth,
  loginWithKeycloak,
  logoutFromKeycloak,
  subscribeAuth,
} from './keycloak'
import type { AuthSnapshot } from './types'
import { readAppEnv } from '../env/runtimeConfig'

const adminRole = readAppEnv('VITE_KEYCLOAK_ROLE_ADMIN')

function normalizeRoleKey(role: string): string {
  return String(role ?? '')
    .trim()
    .toLowerCase()
    .replace(/^\/+/, '')
    .replace(/[\s._:]+/g, '-')
}

function toKeycloakInitErrorMessage(error: unknown): string {
  if (error instanceof Error && error.message.trim().length > 0) return error.message.trim()
  return 'Unable to initialize Keycloak. Check the UI env vars and the client redirect URI settings.'
}

function toFriendlyRoleLabel(role: string): string {
  const value = String(role ?? '').trim()
  const normalizedRole = normalizeRoleKey(value)
  if (!value) return 'User'

  switch (normalizedRole) {
    case 'ngb-admin':
    case 'admin':
    case 'administrator':
    case 'realm-admin':
      return 'Administrator'
    case 'ngb-user':
    case 'user':
      return 'User'
    default: {
      const normalized = value
        .replace(/^ngb[-_:]?/i, '')
        .replace(/[_-]+/g, ' ')
        .trim()

      const source = normalized || value
      return source
        .split(/\s+/g)
        .filter(Boolean)
        .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
        .join(' ')
    }
  }
}

function rolePriority(role: string): number {
  switch (normalizeRoleKey(role)) {
    case 'ngb-admin':
    case 'admin':
    case 'administrator':
    case 'realm-admin':
      return 0
    case 'ngb-user':
    case 'user':
      return 10
    default:
      return 100
  }
}

export const useAuthStore = defineStore('auth', () => {
  const initialized = ref(false)
  const initializing = ref(false)
  const redirecting = ref(false)
  const authenticated = ref(false)
  const token = ref<string | null>(null)
  const subject = ref<string | null>(null)
  const displayName = ref<string | null>(null)
  const preferredUsername = ref<string | null>(null)
  const email = ref<string | null>(null)
  const realmRoles = ref<string[]>([])
  const resourceRoles = ref<Record<string, string[]>>({})
  const roles = ref<string[]>([])
  const error = ref<string | null>(null)

  let initializePromise: Promise<void> | null = null

  function applySnapshot(snapshot: AuthSnapshot): void {
    initialized.value = snapshot.initialized
    authenticated.value = snapshot.authenticated
    token.value = snapshot.token
    subject.value = snapshot.subject
    displayName.value = snapshot.displayName
    preferredUsername.value = snapshot.preferredUsername
    email.value = snapshot.email
    realmRoles.value = snapshot.realmRoles
    resourceRoles.value = snapshot.resourceRoles
    roles.value = snapshot.roles
  }

  subscribeAuth(applySnapshot)
  applySnapshot(getAuthSnapshot())

  const userName = computed(() => displayName.value || preferredUsername.value || email.value || 'User')
  const isAdmin = computed(() => {
    const normalizedConfiguredAdminRole = normalizeRoleKey(adminRole)
    return roles.value.some((role) => {
      const normalizedRole = normalizeRoleKey(role)
      return normalizedRole === 'ngb-admin'
        || normalizedRole === 'admin'
        || normalizedRole === 'administrator'
        || normalizedRole === 'realm-admin'
        || (!!normalizedConfiguredAdminRole && normalizedRole === normalizedConfiguredAdminRole)
    })
  })
  const primaryTechnicalRole = computed(() => {
    const sorted = [...roles.value].sort((a, b) => rolePriority(a) - rolePriority(b) || a.localeCompare(b))
    return sorted[0] ?? ''
  })
  const friendlyRoles = computed(() => {
    const sorted = [...roles.value].sort((a, b) => rolePriority(a) - rolePriority(b) || a.localeCompare(b))
    return Array.from(new Set(sorted.map((role) => toFriendlyRoleLabel(role))))
  })
  const primaryRoleLabel = computed(() => toFriendlyRoleLabel(primaryTechnicalRole.value))
  const primaryRoleIcon = computed<'shield-check' | 'user'>(() =>
    rolePriority(primaryTechnicalRole.value) === 0 ? 'shield-check' : 'user')

  async function initialize(): Promise<void> {
    if (initialized.value) return
    if (initializePromise) return await initializePromise

    initializing.value = true
    error.value = null

    initializePromise = initializeAuth()
      .then((snapshot) => {
        applySnapshot(snapshot)
      })
      .catch((cause) => {
        error.value = toKeycloakInitErrorMessage(cause)
        throw cause
      })
      .finally(() => {
        initializing.value = false
        initializePromise = null
      })

    return await initializePromise
  }

  async function login(targetPath?: string | null): Promise<void> {
    redirecting.value = true
    error.value = null

    try {
      await loginWithKeycloak(targetPath)
    } catch (cause) {
      redirecting.value = false
      error.value = toKeycloakInitErrorMessage(cause)
      throw cause
    }
  }

  async function logout(): Promise<void> {
    redirecting.value = true
    error.value = null

    try {
      await logoutFromKeycloak()
    } catch (cause) {
      redirecting.value = false
      error.value = toKeycloakInitErrorMessage(cause)
      throw cause
    }
  }

  function hasRole(role: string): boolean {
    const value = String(role ?? '').trim()
    return value.length > 0 && roles.value.includes(value)
  }

  return {
    initialized,
    initializing,
    redirecting,
    authenticated,
    token,
    subject,
    displayName,
    preferredUsername,
    email,
    realmRoles,
    resourceRoles,
    roles,
    userName,
    isAdmin,
    primaryTechnicalRole,
    friendlyRoles,
    primaryRoleLabel,
    primaryRoleIcon,
    error,
    initialize,
    login,
    logout,
    hasRole,
  }
})
