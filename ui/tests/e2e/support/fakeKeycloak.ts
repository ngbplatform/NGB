type RuntimeConfigValue = string | boolean | number | null | undefined

type FakeKeycloakConfig = {
  clientId?: string
}

type FakeKeycloakClaims = {
  email?: string
  groups?: string[]
  name?: string
  preferred_username?: string
  realm_access?: {
    roles?: string[]
  }
  resource_access?: Record<string, { roles?: string[] }>
  roles?: string[]
  sub?: string
}

type FakeKeycloakCallback = () => void

declare global {
  interface Window {
    __NGB_RUNTIME_CONFIG__?: Record<string, RuntimeConfigValue>
  }
}

function readEnv(name: string): string {
  const runtimeConfig = typeof window === 'undefined' ? null : window.__NGB_RUNTIME_CONFIG__
  if (runtimeConfig && Object.prototype.hasOwnProperty.call(runtimeConfig, name)) {
    return String(runtimeConfig[name] ?? '').trim()
  }

  const viteEnv = (import.meta as ImportMeta & { env?: Record<string, unknown> }).env ?? {}
  return String(viteEnv[name] ?? '').trim()
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

function readRoles(): string[] {
  const configured = readEnv('VITE_NGB_E2E_AUTH_ROLES')
  return unique((configured || 'ngb-admin ngb-user').split(/[,\s]+/g))
}

function buildClaims(config: FakeKeycloakConfig): FakeKeycloakClaims {
  const roles = readRoles()
  const clientId = String(config.clientId ?? readEnv('VITE_KEYCLOAK_CLIENT_ID') ?? '').trim()

  return {
    sub: readEnv('VITE_NGB_E2E_AUTH_SUBJECT') || 'ngb-e2e-user',
    name: readEnv('VITE_NGB_E2E_AUTH_DISPLAY_NAME') || 'Alex Carter',
    preferred_username: readEnv('VITE_NGB_E2E_AUTH_USERNAME') || 'alex.carter',
    email: readEnv('VITE_NGB_E2E_AUTH_EMAIL') || 'alex.carter@demo.ngbplatform.com',
    roles,
    realm_access: {
      roles,
    },
    resource_access: clientId
      ? {
          [clientId]: {
            roles,
          },
        }
      : {},
    groups: roles.map((role) => `/e2e/${role}`),
  }
}

export default class FakeKeycloak {
  authenticated = false
  idTokenParsed: FakeKeycloakClaims | null = null
  onAuthError: FakeKeycloakCallback | null = null
  onAuthLogout: FakeKeycloakCallback | null = null
  onAuthRefreshError: FakeKeycloakCallback | null = null
  onAuthRefreshSuccess: FakeKeycloakCallback | null = null
  onAuthSuccess: FakeKeycloakCallback | null = null
  onReady: FakeKeycloakCallback | null = null
  onTokenExpired: FakeKeycloakCallback | null = null
  subject: string | null = null
  token: string | null = null
  tokenParsed: FakeKeycloakClaims | null = null

  constructor(private readonly config: FakeKeycloakConfig = {}) {}

  async init(): Promise<boolean> {
    this.applyAuthenticatedState()
    this.onReady?.()
    this.onAuthSuccess?.()
    return true
  }

  async login(): Promise<void> {
    this.applyAuthenticatedState()
    this.onAuthSuccess?.()
  }

  async logout(): Promise<void> {
    this.clearToken()
    this.onAuthLogout?.()
  }

  async updateToken(): Promise<boolean> {
    if (!this.authenticated) return false

    this.token = readEnv('VITE_NGB_E2E_AUTH_TOKEN') || 'ngb-e2e-access-token'
    this.onAuthRefreshSuccess?.()
    return true
  }

  clearToken(): void {
    this.authenticated = false
    this.idTokenParsed = null
    this.subject = null
    this.token = null
    this.tokenParsed = null
  }

  private applyAuthenticatedState(): void {
    const claims = buildClaims(this.config)
    this.authenticated = true
    this.idTokenParsed = claims
    this.subject = claims.sub ?? null
    this.token = readEnv('VITE_NGB_E2E_AUTH_TOKEN') || 'ngb-e2e-access-token'
    this.tokenParsed = claims
  }
}
