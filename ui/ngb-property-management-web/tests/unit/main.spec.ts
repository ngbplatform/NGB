import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => {
  const app = {
    use: vi.fn(),
    mount: vi.fn(),
  }
  app.use.mockReturnValue(app)

  return {
    app,
    createApp: vi.fn(() => app),
    pinia: { kind: 'pinia' },
    createPinia: vi.fn(() => ({ kind: 'pinia' })),
    setActivePinia: vi.fn(),
    auth: {
      authenticated: true,
      error: null as unknown,
      initialize: vi.fn(),
      login: vi.fn(),
    },
    useAuthStore: vi.fn(),
    router: {
      isReady: vi.fn(),
    },
    configureNavigation: vi.fn(),
    configureWorkCenter: vi.fn(),
    configureLookup: vi.fn(),
    configureEditor: vi.fn(),
    configureMetadata: vi.fn(),
    configureReporting: vi.fn(),
    configureCommandPalette: vi.fn(),
    defaultWorkCenter: { kind: 'default-work-center' },
    defaultLookup: { kind: 'default-lookup' },
    defaultReporting: { kind: 'default-reporting' },
    editorConfig: { kind: 'pm-editor' },
    metadataConfig: { kind: 'pm-metadata' },
    navigationConfig: { kind: 'pm-navigation' },
    paletteConfig: { kind: 'pm-palette' },
  }
})

vi.mock('vue', () => ({ createApp: mocks.createApp }))
vi.mock('pinia', () => ({
  createPinia: mocks.createPinia,
  setActivePinia: mocks.setActivePinia,
}))
vi.mock('@ngbplatform/ui', () => ({
  configureNgbCommandPalette: mocks.configureCommandPalette,
  configureNgbEditor: mocks.configureEditor,
  configureNgbLookup: mocks.configureLookup,
  configureNgbMetadata: mocks.configureMetadata,
  configureNgbNavigation: mocks.configureNavigation,
  configureNgbReporting: mocks.configureReporting,
  configureNgbWorkCenter: mocks.configureWorkCenter,
  createDefaultNgbLookupConfig: () => mocks.defaultLookup,
  createDefaultNgbReportingConfig: () => mocks.defaultReporting,
  createDefaultNgbWorkCenterConfig: () => mocks.defaultWorkCenter,
  useAuthStore: mocks.useAuthStore,
}))
vi.mock('@ngbplatform/ui/styles', () => ({}))
vi.mock('../../src/App.vue', () => ({ default: { name: 'PmAppStub' } }))
vi.mock('../../src/router/router', () => ({ router: mocks.router }))
vi.mock('../../src/command-palette/config', () => ({
  createPmCommandPaletteConfig: () => mocks.paletteConfig,
}))
vi.mock('../../src/metadata/framework', () => ({
  createPmMetadataConfig: () => mocks.metadataConfig,
}))
vi.mock('../../src/editor/framework', () => ({
  createPmEditorConfig: () => mocks.editorConfig,
}))
vi.mock('../../src/navigation/framework', () => ({
  createPmNavigationConfig: () => mocks.navigationConfig,
}))

describe('property management application bootstrap', () => {
  const originalWindow = globalThis.window

  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
    mocks.app.use.mockReturnValue(mocks.app)
    mocks.createApp.mockReturnValue(mocks.app)
    mocks.createPinia.mockReturnValue(mocks.pinia)
    mocks.useAuthStore.mockReturnValue(mocks.auth)
    mocks.router.isReady.mockResolvedValue(undefined)
    mocks.auth.authenticated = true
    mocks.auth.error = null
    mocks.auth.initialize.mockResolvedValue(undefined)
    mocks.auth.login.mockResolvedValue(undefined)
    globalThis.window = {
      location: {
        pathname: '/catalogs/pm.property',
        search: '?trash=active',
      },
    } as typeof window
  })

  afterEach(() => {
    globalThis.window = originalWindow as typeof window
  })

  it('configures all framework adapters and mounts after authentication', async () => {
    await import('../../src/main')
    await vi.waitFor(() => expect(mocks.app.mount).toHaveBeenCalledWith('#app'))

    expect(mocks.setActivePinia).toHaveBeenCalledWith(mocks.pinia)
    expect(mocks.configureNavigation).toHaveBeenCalledWith(mocks.navigationConfig)
    expect(mocks.configureWorkCenter).toHaveBeenCalledWith(mocks.defaultWorkCenter)
    expect(mocks.configureLookup).toHaveBeenCalledWith(mocks.defaultLookup)
    expect(mocks.configureEditor).toHaveBeenCalledWith(mocks.editorConfig)
    expect(mocks.configureMetadata).toHaveBeenCalledWith(mocks.metadataConfig)
    expect(mocks.configureReporting).toHaveBeenCalledWith(mocks.defaultReporting)
    expect(mocks.configureCommandPalette).toHaveBeenCalledWith(mocks.paletteConfig)
    expect(mocks.app.use).toHaveBeenNthCalledWith(1, mocks.pinia)
    expect(mocks.app.use).toHaveBeenNthCalledWith(2, mocks.router)
    expect(mocks.router.isReady).toHaveBeenCalledTimes(1)
  })

  it('starts login and stops before loading the application when unauthenticated', async () => {
    mocks.auth.authenticated = false

    await import('../../src/main')
    await vi.waitFor(() => expect(mocks.auth.login).toHaveBeenCalledWith('/catalogs/pm.property?trash=active'))

    expect(mocks.createApp).not.toHaveBeenCalled()
    expect(mocks.configureMetadata).not.toHaveBeenCalled()
  })

  it('mounts the retry state when authentication initialization fails', async () => {
    mocks.auth.authenticated = false
    mocks.auth.error = new Error('identity unavailable')
    mocks.auth.initialize.mockRejectedValueOnce(mocks.auth.error)

    await import('../../src/main')
    await vi.waitFor(() => expect(mocks.app.mount).toHaveBeenCalledWith('#app'))

    expect(mocks.auth.login).not.toHaveBeenCalled()
    expect(mocks.configureMetadata).toHaveBeenCalledWith(mocks.metadataConfig)
  })
})
