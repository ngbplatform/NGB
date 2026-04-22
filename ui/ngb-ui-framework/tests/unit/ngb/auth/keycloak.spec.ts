import { afterEach, describe, expect, it, vi } from 'vitest'

type MockKeycloakInstance = {
  authenticated: boolean
  clearToken: ReturnType<typeof vi.fn>
  config?: Record<string, unknown>
  idTokenParsed: Record<string, unknown> | null
  init: ReturnType<typeof vi.fn>
  login: ReturnType<typeof vi.fn>
  logout: ReturnType<typeof vi.fn>
  onAuthError?: (() => void) | null
  onAuthLogout?: (() => void) | null
  onAuthRefreshError?: (() => void) | null
  onAuthRefreshSuccess?: (() => void) | null
  onAuthSuccess?: (() => void) | null
  onReady?: (() => void) | null
  onTokenExpired?: (() => void) | null
  subject: string | null
  token: string | null
  tokenParsed: Record<string, unknown> | null
  updateToken: ReturnType<typeof vi.fn>
}

function createMockKeycloakInstance(
  overrides: Partial<MockKeycloakInstance> = {},
): MockKeycloakInstance {
  const instance: MockKeycloakInstance = {
    authenticated: false,
    clearToken: vi.fn(),
    idTokenParsed: null,
    init: vi.fn(async () => false),
    login: vi.fn(async () => undefined),
    logout: vi.fn(async () => undefined),
    subject: null,
    token: null,
    tokenParsed: null,
    updateToken: vi.fn(async () => true),
    ...overrides,
  }

  instance.clearToken.mockImplementation(() => {
    instance.authenticated = false
    instance.token = null
    instance.subject = null
  })

  return instance
}

async function importKeycloakModule(options: {
  env?: Record<string, string>
  instance?: MockKeycloakInstance
  windowUrl?: string
} = {}) {
  vi.resetModules()
  vi.unstubAllEnvs()
  vi.unstubAllGlobals()

  const instance = options.instance ?? createMockKeycloakInstance()
  const fetchMock = vi.fn(async () => new Response(null, { status: 204 }))
  const keycloakCtor = vi.fn(function MockKeycloak(config: Record<string, unknown>) {
    instance.config = config
    return instance
  })

  vi.doMock('keycloak-js', () => ({
    default: keycloakCtor,
  }))

  vi.stubGlobal('fetch', fetchMock)
  vi.stubGlobal('window', {
    location: new URL(options.windowUrl ?? 'https://app.example/app/home#draft'),
  })

  vi.stubEnv('VITE_KEYCLOAK_URL', 'https://identity.example')
  vi.stubEnv('VITE_KEYCLOAK_REALM', 'ngb-demo')
  vi.stubEnv('VITE_KEYCLOAK_CLIENT_ID', 'ngb-web-client')
  vi.stubEnv('BASE_URL', '/portal/')

  for (const [key, value] of Object.entries(options.env ?? {})) {
    vi.stubEnv(key, value)
  }

  const module = await import('../../../../src/ngb/auth/keycloak')
  return { module, instance, keycloakCtor, fetchMock }
}

async function flushMicrotasks() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('keycloak auth adapter', () => {
  afterEach(() => {
    vi.resetModules()
    vi.doUnmock('keycloak-js')
    vi.unstubAllEnvs()
    vi.unstubAllGlobals()
  })

  it('fails fast during import when required env vars are missing', async () => {
    vi.resetModules()
    vi.doMock('keycloak-js', () => ({
      default: vi.fn(),
    }))
    vi.stubEnv('VITE_KEYCLOAK_REALM', 'ngb-demo')
    vi.stubEnv('VITE_KEYCLOAK_CLIENT_ID', 'ngb-web-client')

    await expect(import('../../../../src/ngb/auth/keycloak')).rejects.toThrow(
      'Missing required env var: VITE_KEYCLOAK_URL',
    )
  })

  it('initializes keycloak with silent check-sso options and merges identity roles into the auth snapshot', async () => {
    const instance = createMockKeycloakInstance()
    instance.init.mockImplementation(async () => {
      instance.authenticated = true
      instance.token = 'access-token'
      instance.subject = 'subject-1'
      instance.tokenParsed = {
        sub: 'access-subject',
        preferred_username: 'jdoe',
        role: 'pm-manager',
        roles: 'ngb-user realm-admin',
        realm_access: {
          roles: ['finance-reader'],
        },
        resource_access: {
          'ngb-web-client': {
            roles: ['app-user'],
          },
        },
      }
      instance.idTokenParsed = {
        given_name: 'Jane',
        family_name: 'Doe',
        email: 'jane@example.com',
        groups: ['/teams/ngb-admin', '/teams/ngb-user'],
      }
      return true
    })

    const { module, instance: importedInstance, keycloakCtor } = await importKeycloakModule({
      env: {
        VITE_KEYCLOAK_ON_LOAD: 'check-sso',
        VITE_KEYCLOAK_SILENT_CHECK_SSO_ENABLED: 'true',
      },
      instance,
    })

    const snapshot = await module.initializeAuth()

    expect(keycloakCtor).toHaveBeenCalledWith({
      url: 'https://identity.example',
      realm: 'ngb-demo',
      clientId: 'ngb-web-client',
    })
    expect(importedInstance.init).toHaveBeenCalledWith({
      onLoad: 'check-sso',
      pkceMethod: 'S256',
      responseMode: 'fragment',
      checkLoginIframe: false,
      silentCheckSsoRedirectUri: 'https://app.example/portal/silent-check-sso.html',
      silentCheckSsoFallback: false,
    })
    expect(snapshot).toMatchObject({
      initialized: true,
      authenticated: true,
      token: 'access-token',
      subject: 'subject-1',
      displayName: 'Jane Doe',
      preferredUsername: 'jdoe',
      email: 'jane@example.com',
      realmRoles: ['finance-reader'],
      resourceRoles: {
        'ngb-web-client': ['app-user'],
      },
    })
    expect(snapshot.roles).toEqual([
      'pm-manager',
      'ngb-user',
      'realm-admin',
      'finance-reader',
      'app-user',
      'ngb-admin',
    ])
    expect(module.getAuthSnapshot()).toEqual(snapshot)
  })

  it('deduplicates concurrent token refreshes and supports forced refresh requests', async () => {
    let resolveRefresh!: () => void
    const refreshPromise = new Promise<void>((resolve) => {
      resolveRefresh = resolve
    })

    const instance = createMockKeycloakInstance({
      authenticated: true,
      subject: 'subject-1',
      token: 'stale-token',
    })

    instance.init.mockResolvedValueOnce(true)
    instance.updateToken.mockImplementationOnce(async () => {
      await refreshPromise
      instance.token = 'fresh-token'
      return true
    })

    const { module } = await importKeycloakModule({ instance })
    await module.initializeAuth()

    const first = module.getAccessToken(15)
    const second = module.getAccessToken(15)

    expect(instance.updateToken).toHaveBeenCalledTimes(1)
    expect(instance.updateToken).toHaveBeenCalledWith(15)

    resolveRefresh()

    await expect(first).resolves.toBe('fresh-token')
    await expect(second).resolves.toBe('fresh-token')

    instance.updateToken.mockClear()
    instance.updateToken.mockImplementationOnce(async () => {
      instance.token = 'forced-token'
      return true
    })

    await expect(module.forceRefreshAccessToken()).resolves.toBe('forced-token')
    expect(instance.updateToken).toHaveBeenCalledWith(-1)
  })

  it('clears the auth snapshot when token refresh fails after expiration', async () => {
    const instance = createMockKeycloakInstance({
      authenticated: true,
      subject: 'subject-1',
      token: 'expiring-token',
      tokenParsed: {
        preferred_username: 'jdoe',
      },
    })

    instance.init.mockResolvedValueOnce(true)

    const { module } = await importKeycloakModule({ instance })
    await module.initializeAuth()

    const listener = vi.fn()
    module.subscribeAuth(listener)

    instance.updateToken.mockRejectedValueOnce(new Error('refresh failed'))
    instance.onTokenExpired?.()

    await flushMicrotasks()

    expect(instance.clearToken).toHaveBeenCalled()
    expect(listener.mock.lastCall?.[0]).toMatchObject({
      authenticated: false,
      token: null,
      subject: null,
    })
  })

  it('normalizes login redirects for relative targets and preserves explicit absolute urls', async () => {
    const instance = createMockKeycloakInstance()
    instance.init.mockResolvedValue(true)

    const { module } = await importKeycloakModule({ instance })

    await module.loginWithKeycloak('/reports/occupancy')
    expect(instance.login).toHaveBeenNthCalledWith(1, {
      redirectUri: 'https://app.example/reports/occupancy',
    })

    await module.loginWithKeycloak('https://external.example/reports/occupancy')
    expect(instance.login).toHaveBeenNthCalledWith(2, {
      redirectUri: 'https://external.example/reports/occupancy',
    })
  })

  it('logs out known admin consoles before keycloak logout and normalizes same-host post-logout redirects', async () => {
    const instance = createMockKeycloakInstance({
      authenticated: true,
      token: 'access-token',
    })
    instance.init.mockResolvedValueOnce(true)

    const { module, fetchMock } = await importKeycloakModule({
      env: {
        VITE_WATCHDOG_URL: 'http://watchdog.example:8080/dashboard',
        VITE_BACKGROUND_JOB_URL: 'https://jobs.example/admin',
        VITE_KEYCLOAK_POST_LOGOUT_REDIRECT_URL: 'http://app.example/post-logout',
      },
      instance,
    })

    await module.logoutFromKeycloak()

    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(fetchMock.mock.calls).toEqual(expect.arrayContaining([
      [
        'http://watchdog.example:8080/account/local-logout',
        expect.objectContaining({
          method: 'POST',
          credentials: 'include',
          mode: 'cors',
          cache: 'no-store',
          keepalive: true,
        }),
      ],
      [
        'https://jobs.example/account/local-logout',
        expect.objectContaining({
          method: 'POST',
          credentials: 'include',
          mode: 'cors',
          cache: 'no-store',
          keepalive: true,
        }),
      ],
    ]))
    expect(instance.logout).toHaveBeenCalledWith({
      redirectUri: 'https://app.example/post-logout',
    })
  })
})
