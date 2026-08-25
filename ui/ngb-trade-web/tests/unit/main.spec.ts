import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => {
  const app = { use: vi.fn(), mount: vi.fn() }
  app.use.mockReturnValue(app)
  return {
    app, createApp: vi.fn(() => app), pinia: { kind: 'pinia' }, createPinia: vi.fn(), setActivePinia: vi.fn(),
    auth: { authenticated: true, error: null as unknown, initialize: vi.fn(), login: vi.fn() },
    useAuthStore: vi.fn(), router: { isReady: vi.fn() },
    configureNavigation: vi.fn(), configureWorkCenter: vi.fn(), configureLookup: vi.fn(),
    configureEditor: vi.fn(), configureMetadata: vi.fn(), configureReporting: vi.fn(), configurePalette: vi.fn(),
    workCenter: {}, lookup: {}, reporting: {}, editor: {}, metadata: {}, palette: {},
  }
})

vi.mock('vue', () => ({ createApp: mocks.createApp }))
vi.mock('pinia', () => ({ createPinia: mocks.createPinia, setActivePinia: mocks.setActivePinia }))
vi.mock('@ngbplatform/ui', () => ({
  configureNgbCommandPalette: mocks.configurePalette,
  configureNgbEditor: mocks.configureEditor,
  configureNgbLookup: mocks.configureLookup,
  configureNgbMetadata: mocks.configureMetadata,
  configureNgbNavigation: mocks.configureNavigation,
  configureNgbReporting: mocks.configureReporting,
  configureNgbWorkCenter: mocks.configureWorkCenter,
  createDefaultNgbLookupConfig: () => mocks.lookup,
  createDefaultNgbReportingConfig: () => mocks.reporting,
  createDefaultNgbWorkCenterConfig: () => mocks.workCenter,
  useAuthStore: mocks.useAuthStore,
}))
vi.mock('@ngbplatform/ui/styles', () => ({}))
vi.mock('../../src/App.vue', () => ({ default: { name: 'TradeAppStub' } }))
vi.mock('../../src/router/router', () => ({ router: mocks.router }))
vi.mock('../../src/command-palette/config', () => ({ createTradeCommandPaletteConfig: () => mocks.palette }))
vi.mock('../../src/metadata/framework', () => ({ createTradeMetadataConfig: () => mocks.metadata }))
vi.mock('../../src/editor/framework', () => ({ createTradeEditorConfig: () => mocks.editor }))

describe('trade application bootstrap', () => {
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
    globalThis.window = { location: { pathname: '/documents/trd.sales_invoice', search: '?status=draft' } } as typeof window
  })

  afterEach(() => { globalThis.window = originalWindow as typeof window })

  it('configures framework adapters and mounts after authentication', async () => {
    await import('../../src/main')
    await vi.waitFor(() => expect(mocks.app.mount).toHaveBeenCalledWith('#app'))
    expect(mocks.setActivePinia).toHaveBeenCalledWith(mocks.pinia)
    expect(mocks.configureNavigation).toHaveBeenCalledWith()
    expect(mocks.configureWorkCenter).toHaveBeenCalledWith(mocks.workCenter)
    expect(mocks.configureLookup).toHaveBeenCalledWith(mocks.lookup)
    expect(mocks.configureEditor).toHaveBeenCalledWith(mocks.editor)
    expect(mocks.configureMetadata).toHaveBeenCalledWith(mocks.metadata)
    expect(mocks.configureReporting).toHaveBeenCalledWith(mocks.reporting)
    expect(mocks.configurePalette).toHaveBeenCalledWith(mocks.palette)
    expect(mocks.app.use).toHaveBeenNthCalledWith(1, mocks.pinia)
    expect(mocks.app.use).toHaveBeenNthCalledWith(2, mocks.router)
    expect(mocks.router.isReady).toHaveBeenCalledTimes(1)
  })

  it('redirects unauthenticated users to login before loading the app', async () => {
    mocks.auth.authenticated = false
    await import('../../src/main')
    await vi.waitFor(() => expect(mocks.auth.login).toHaveBeenCalledWith('/documents/trd.sales_invoice?status=draft'))
    expect(mocks.createApp).not.toHaveBeenCalled()
  })

  it('mounts the retry state after initialization failure', async () => {
    mocks.auth.authenticated = false
    mocks.auth.error = new Error('offline')
    mocks.auth.initialize.mockRejectedValueOnce(mocks.auth.error)
    await import('../../src/main')
    await vi.waitFor(() => expect(mocks.app.mount).toHaveBeenCalledWith('#app'))
    expect(mocks.auth.login).not.toHaveBeenCalled()
  })
})
