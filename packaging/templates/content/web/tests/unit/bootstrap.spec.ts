import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { startApplication } from '../../src/bootstrap'

const state = vi.hoisted(() => {
  const app = { use: vi.fn(), mount: vi.fn() }
  app.use.mockReturnValue(app)
  return {
    app,
    auth: { initialize: vi.fn(), authenticated: true, login: vi.fn() },
    access: { load: vi.fn() },
    router: { isReady: vi.fn() },
    createApp: vi.fn(() => app),
  }
})

vi.mock('vue', async importOriginal => ({ ...await importOriginal<typeof import('vue')>(), createApp: state.createApp }))
vi.mock('pinia', () => ({ createPinia: vi.fn(), setActivePinia: vi.fn() }))
vi.mock('@ngbplatform/ui', () => ({
  configureNgbEditor: vi.fn(), configureNgbNavigation: vi.fn(), configureNgbWorkCenter: vi.fn(),
  createDefaultNgbWorkCenterConfig: vi.fn(), executeDocumentAction: vi.fn(), getDocumentById: vi.fn(),
  getDocumentEditorState: vi.fn(), getDocumentEffects: vi.fn(), getDocumentGraph: vi.fn(), getEntityAuditLog: vi.fn(),
  useAuthStore: () => state.auth, useAccessStore: () => state.access,
}))
vi.mock('../../src/router', () => ({ router: state.router }))
vi.mock('../../src/App.vue', () => ({ default: {} }))

beforeEach(() => {
  vi.clearAllMocks()
  state.auth.authenticated = true
  state.auth.initialize.mockResolvedValue(undefined)
  state.access.load.mockResolvedValue(undefined)
  state.router.isReady.mockResolvedValue(undefined)
  document.body.innerHTML = '<div id="app"></div>'
})
afterEach(() => { document.body.innerHTML = '' })

it('loads access before mounting protected pages and waits for the router', async () => {
  let release: () => void
  state.access.load.mockReturnValue(new Promise<void>(resolve => { release = resolve }))
  const started = startApplication()
  await vi.waitFor(() => expect(state.access.load).toHaveBeenCalledOnce())
  expect(state.createApp).not.toHaveBeenCalled()
  release!()
  await started
  expect(state.app.mount).toHaveBeenCalledWith('#app')
  expect(state.router.isReady).toHaveBeenCalledOnce()
})

it('redirects an unauthenticated user before creating application services', async () => {
  state.auth.authenticated = false
  await startApplication()
  expect(state.auth.login).toHaveBeenCalledOnce()
  expect(state.access.load).not.toHaveBeenCalled()
  expect(state.createApp).not.toHaveBeenCalled()
})

it('renders an identity startup error', async () => {
  state.auth.initialize.mockRejectedValue(new Error('Identity unavailable'))
  await startApplication()
  expect(document.getElementById('app')?.textContent).toBe('Identity unavailable')
})

it('renders unexpected startup failures without exposing arbitrary values', async () => {
  state.auth.initialize.mockRejectedValue('unexpected value')
  await startApplication()
  expect(document.getElementById('app')?.textContent).toBe('Application startup failed.')
})

it('handles startup failure even when the host element is unavailable', async () => {
  document.body.innerHTML = ''
  state.auth.initialize.mockRejectedValue(new Error('Identity unavailable'))
  await startApplication()
  expect(state.auth.initialize).toHaveBeenCalledOnce()
  expect(state.createApp).not.toHaveBeenCalled()
})
