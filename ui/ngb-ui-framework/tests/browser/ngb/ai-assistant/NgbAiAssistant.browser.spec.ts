import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { defineComponent, h } from 'vue'
import NgbSiteShell from '../../../../src/ngb/site/NgbSiteShell.vue'
import { getAssistantSettings, streamAssistant } from '../../../../src/ngb/ai-assistant/api'
import { getFeatures } from '../../../../src/ngb/features/api'

vi.mock('../../../../src/ngb/ai-assistant/api', () => ({ getAssistantSettings: vi.fn(), streamAssistant: vi.fn() }))
vi.mock('../../../../src/ngb/features/api', () => ({ getFeatures: vi.fn() }))

beforeEach(() => {
  vi.resetAllMocks()
  vi.mocked(getFeatures).mockResolvedValue(['Mcp', 'AiAssistant'].map(code => ({ code, displayName: code, group: 'Integrations', enabled: true })))
  vi.mocked(getAssistantSettings).mockResolvedValue({
    provider: 'Ollama', model: 'qwen3.5:4b', maxMessages: 20, maxMessageCharacters: 8000,
    maxInputBytes: 32768, maxOutputCharacters: 16384,
  })
  vi.mocked(streamAssistant).mockImplementation(async (_, event) => {
    event({ type: 'text', text: '**Seven** records. <script>alert(1)</script>' })
    event({ type: 'source', source: { kind: 'report', typeCode: 'pm.aging', title: 'Aging report' } })
    event({ type: 'done' })
  })
})

async function mountShell() {
  const navigate = vi.fn()
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/:pathMatch(.*)*', component: defineComponent(() => () => h('div')) }] })
  await router.push('/catalogs/pm.property')
  await router.isReady()
  const view = await render(NgbSiteShell, {
    props: { moduleTitle: 'Property Management', productTitle: 'NGB', pinned: [], recent: [], nodes: [], selectedId: null, onNavigate: navigate },
    slots: { default: '<button type="button">Application action</button>' },
    global: { plugins: [createPinia(), router] },
  })
  await view.getByRole('button', { name: 'AI Assistant', exact: true }).click()
  await expect.element(view.getByRole('textbox', { name: 'Ask a question' })).toBeVisible()
  return { view, navigate }
}

test('desktop panel preserves application interaction, streams safe Markdown and follows real sources', async () => {
  await page.viewport(1440, 900)
  const { view, navigate } = await mountShell()
  expect(document.querySelector('[role="dialog"]')).toBeNull()
  await view.getByRole('button', { name: 'Application action' }).click()
  await view.getByRole('textbox', { name: 'Ask a question' }).fill('Show my records')
  await view.getByRole('button', { name: 'Send', exact: true }).click()

  await expect.element(view.getByRole('button', { name: 'Aging report' })).toBeVisible()
  expect(document.querySelector('.assistant-markdown strong')?.textContent).toBe('Seven')
  expect(document.querySelector('.assistant-markdown script')).toBeNull()
  expect(vi.mocked(streamAssistant).mock.calls[0]![0].context).toEqual({ kind: 'catalog', typeCode: 'pm.property' })
  await view.getByRole('button', { name: 'Aging report' }).click()
  expect(navigate).toHaveBeenCalledWith('/reports/pm.aging')

  await view.getByRole('button', { name: 'Close assistant' }).click()
  await view.getByRole('button', { name: 'AI Assistant', exact: true }).click()
  await expect.element(view.getByRole('button', { name: 'Aging report' })).toBeVisible()
  await expect.element(view.getByRole('textbox', { name: 'Ask a question' })).toHaveFocus()
  await view.getByRole('button', { name: 'New chat' }).click()
  await expect.element(view.getByText('How can I help?')).toBeVisible()
})

test('mobile panel fills the viewport and Stop cancels the active request', async () => {
  await page.viewport(390, 844)
  let cancelled = false
  vi.mocked(streamAssistant).mockImplementation(async (_, event, signal) => {
    event({ type: 'text', text: 'Partial answer' })
    await new Promise<void>(resolve => signal.addEventListener('abort', () => {
      cancelled = true
      resolve()
    }, { once: true }))
  })
  const { view } = await mountShell()
  const dialog = document.querySelector('[role="dialog"]')
  expect(dialog).not.toBeNull()
  expect(document.querySelector('[data-testid="drawer-panel"]')!.getBoundingClientRect().width).toBe(390)
  await view.getByRole('textbox', { name: 'Ask a question' }).fill('Question')
  await view.getByRole('button', { name: 'Send', exact: true }).click()
  await expect.element(view.getByText('Partial answer')).toBeVisible()

  await view.getByRole('button', { name: 'Stop', exact: true }).click()

  expect(cancelled).toBe(true)
  await expect.element(view.getByText('Response stopped.')).toBeVisible()
  await expect.element(view.getByRole('textbox', { name: 'Ask a question' })).toBeEnabled()
})

test('failed turns show a retry action and never look complete', async () => {
  await page.viewport(1440, 900)
  vi.mocked(streamAssistant).mockRejectedValueOnce(new Error('Connection ended'))
  const { view } = await mountShell()
  await view.getByRole('textbox', { name: 'Ask a question' }).fill('Question')
  await view.getByRole('button', { name: 'Send', exact: true }).click()
  await expect.element(view.getByRole('alert')).toHaveTextContent('Connection ended')

  await view.getByRole('button', { name: 'Retry', exact: true }).click()

  await expect.element(view.getByRole('button', { name: 'Aging report' })).toBeVisible()
  expect(streamAssistant).toHaveBeenCalledTimes(2)
})
