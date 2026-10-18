import { page } from 'vitest/browser'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { defineComponent, h } from 'vue'
import NgbSiteShell from '../../../../src/ngb/site/NgbSiteShell.vue'
import { getAssistantSettings, streamAssistant } from '../../../../src/ngb/ai-assistant/api'
import { getFeatures } from '../../../../src/ngb/features/api'
import { configureNgbNavigation, NgbSourceKinds } from '../../../../src/navigation'
import { configureNgbEditor, type EditorRoutingConfig } from '../../../../src/index'

vi.mock('../../../../src/ngb/ai-assistant/api', () => ({ getAssistantSettings: vi.fn(), streamAssistant: vi.fn() }))
vi.mock('../../../../src/ngb/features/api', () => ({ getFeatures: vi.fn() }))

function configureEditorRouting(routing: EditorRoutingConfig = {}): void {
  configureNgbEditor({
    routing,
    loadDocumentById: vi.fn(),
    loadDocumentEffects: vi.fn(),
    loadDocumentGraph: vi.fn(),
    loadEntityAuditLog: vi.fn(),
    documentActions: { loadEditorState: vi.fn(), execute: vi.fn() },
  })
}

afterEach(() => {
  configureNgbNavigation()
  configureEditorRouting()
})

beforeEach(() => {
  vi.resetAllMocks()
  configureNgbNavigation()
  configureEditorRouting()
  vi.mocked(getFeatures).mockResolvedValue(['Mcp', 'AiAssistant'].map(code => ({ code, displayName: code, group: 'Integrations', enabled: true })))
  vi.mocked(getAssistantSettings).mockResolvedValue({
    provider: 'Ollama', model: 'qwen3.5:9b', maxMessages: 20, maxMessageCharacters: 8000,
    maxInputBytes: 32768, maxOutputCharacters: 16384,
  })
  vi.mocked(streamAssistant).mockImplementation(async (_, event) => {
    event({ type: 'text', text: '**Seven** records. <script>alert(1)</script>' })
    event({ type: 'source', source: { kind: 'report', typeCode: 'pm.aging', title: 'Aging report' } })
    event({ type: 'done' })
  })
})

async function mountShell(initialPath = '/catalogs/pm.property') {
  const navigate = vi.fn()
  const component = defineComponent(() => () => h('div'))
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/customers/:customerId', name: 'customer', component },
      { path: '/:pathMatch(.*)*', component },
    ],
  })
  await router.push(initialPath)
  await router.isReady()
  const view = await render(NgbSiteShell, {
    props: { moduleTitle: 'Property Management', productTitle: 'NGB', pageTitle: 'Properties & Units', pinned: [], recent: [], nodes: [], selectedId: null, onNavigate: navigate },
    slots: { default: '<button type="button">Application action</button>' },
    global: { plugins: [createPinia(), router] },
  })
  await view.getByRole('button', { name: 'AI Assistant', exact: true }).click()
  await expect.element(view.getByRole('textbox', { name: 'Ask a question' })).toBeVisible()
  return { view, navigate, router }
}

test('desktop panel preserves application interaction, streams safe Markdown and follows real sources', async () => {
  vi.mocked(streamAssistant).mockImplementation(async (_, event) => {
    event({ type: 'source', source: { kind: 'report', typeCode: 'pm.aging', title: 'Aging report' } })
    event({ type: 'text', text: '**Seven** records. <script>alert(1)</script>\n\n[Aging](ngb-source:report:pm.aging)' })
    event({ type: 'done' })
  })
  await page.viewport(1440, 900)
  const { view, navigate } = await mountShell()
  expect(document.querySelector('[role="dialog"]')).toBeNull()
  await expect.element(view.getByRole('checkbox', { name: 'Focus on Properties & Units' })).toBeChecked()
  const header = document.querySelector('[data-testid="ai-assistant-header"]')!.getBoundingClientRect()
  const topbar = document.querySelector('[data-testid="site-topbar"]')!.getBoundingClientRect()
  expect(header.bottom).toBe(topbar.bottom)
  await view.getByRole('button', { name: 'Application action' }).click()
  await view.getByRole('textbox', { name: 'Ask a question' }).fill('Show my records')
  await view.getByRole('button', { name: 'Send', exact: true }).click()

  await expect.element(view.getByRole('link', { name: 'Aging report' })).toBeVisible()
  await expect.element(view.getByText('qwen3.5:9b')).not.toBeInTheDocument()
  expect(document.querySelector('.assistant-markdown strong')?.textContent).toBe('Seven')
  expect(document.querySelector('.assistant-markdown script')).toBeNull()
  expect(vi.mocked(streamAssistant).mock.calls[0]![0].context).toEqual({ kind: 'catalog', typeCode: 'pm.property' })
  await expect.element(view.getByRole('link', { name: 'Aging report' })).toHaveAttribute('href', '/reports/pm.aging')
  await view.getByRole('link', { name: 'Aging report' }).click()
  expect(navigate).toHaveBeenCalledWith('/reports/pm.aging')

  await view.getByRole('button', { name: 'Close assistant' }).click()
  await view.getByRole('button', { name: 'AI Assistant', exact: true }).click()
  await expect.element(view.getByRole('link', { name: 'Aging report' })).toBeVisible()
  await expect.element(view.getByRole('textbox', { name: 'Ask a question' })).toHaveFocus()
  await view.getByRole('button', { name: 'New chat' }).click()
  await expect.element(view.getByText('How can I help?')).toBeVisible()
})

test('does not display discovered sources for a numeric answer and renders a scrollable Markdown table', async () => {
  await page.viewport(1440, 900)
  vi.mocked(streamAssistant).mockImplementation(async (_, event) => {
    event({ type: 'source', source: { kind: 'report', typeCode: 'pm.aging', title: 'Aging report' } })
    event({ type: 'text', text: '| Item | Date | Amount |\n| --- | --- | ---: |\n| Balance | 2026-05-10T20:43:34.293462Z | 331 |' })
    event({ type: 'done' })
  })
  const { view } = await mountShell()
  await view.getByRole('textbox', { name: 'Ask a question' }).fill('Show the balance')
  await view.getByRole('button', { name: 'Send', exact: true }).click()

  await expect.element(view.getByRole('link', { name: 'Aging report' })).not.toBeInTheDocument()
  expect(document.querySelector('[aria-label="Sources"]')).toBeNull()
  const table = document.querySelector<HTMLDivElement>('.assistant-table')!
  expect(table.getAttribute('tabindex')).toBe('0')
  expect(table.querySelectorAll('th')).toHaveLength(3)
  expect(table.textContent).not.toContain('293462Z')
  expect(table.getBoundingClientRect().width).toBeLessThanOrEqual(480)
  expect(getComputedStyle(table).overflowX).toBe('auto')
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

test('wide answers keep every labelled field inside the panel and preserve literal filenames', async () => {
  await page.viewport(1440, 900)
  vi.mocked(streamAssistant).mockImplementation(async (_, event) => {
    event({ type: 'value', value: { reference: '{{ngb-value:abcdef012345-0}}', kind: 'text', value: 'Screenshot 2026-10-02 at 5.46.52 PM.png' } })
    event({ type: 'value', value: { reference: '{{ngb-value:abcdef012345-1}}', kind: 'bytes', value: '723275' } })
    event({ type: 'text', text: '| Lease | Tenant | Property | Start | Attachment | Size |\n| --- | --- | --- | --- | --- | --- |\n| Lease 1 | Alex **Carter** | 100 Hudson Ave, Dataset demo, Hoboken, NJ 07030 #102 | 2026-05-10 | {{ngb-value:abcdef012345-0}} | {{ngb-value:abcdef012345-1}} |' })
    event({ type: 'done' })
  })
  const { view } = await mountShell()
  await view.getByRole('textbox', { name: 'Ask a question' }).fill('Show the lease and attachment')
  await view.getByRole('button', { name: 'Send', exact: true }).click()

  await expect.element(view.getByText('723,275 bytes', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Screenshot 2026-10-02 at 5.46.52 PM.png', { exact: true })).toBeVisible()
  const table = document.querySelector<HTMLDivElement>('.assistant-table')!
  const cells = [...table.querySelectorAll('td')]
  expect(cells).toHaveLength(6)
  expect(cells.map(cell => cell.dataset.label)).toEqual(['Lease', 'Tenant', 'Property', 'Start', 'Attachment', 'Size'])
  expect(table.scrollWidth).toBeLessThanOrEqual(table.clientWidth + 1)
  for (const cell of cells) {
    expect(cell.getBoundingClientRect().right).toBeLessThanOrEqual(table.getBoundingClientRect().right + 1)
  }
  expect(cells[1]?.querySelector('.assistant-cell-value')?.textContent).toBe('Alex Carter')
})

test('payment links use the returned Display and real record route instead of model URLs', async () => {
  await page.viewport(1440, 900)
  const id = '11111111-1111-4111-8111-111111111111'
  const display = 'Receivable Payment RP-2026-000014 8/24/2025'
  vi.mocked(streamAssistant).mockImplementation(async (_, event) => {
    event({ type: 'source', source: { kind: 'document', typeCode: 'pm.receivable_payment', id, title: display } })
    event({ type: 'text', text: `[wrong label](ngb-source:document:pm.receivable_payment:${id})\n\nhttps://app.noble-gateway.com/pm.receivable_payment/RP-2026-000014` })
    event({ type: 'done' })
  })
  const { view, navigate } = await mountShell()
  await view.getByRole('textbox', { name: 'Ask a question' }).fill('give the link of RP-2026-000014')
  await view.getByRole('button', { name: 'Send', exact: true }).click()

  const reply = document.querySelector('.assistant-markdown')!
  const link = reply.querySelector('a')!
  expect(link.textContent).toBe(display)
  expect(link.getAttribute('href')).toBe(`/documents/pm.receivable_payment/${id}`)
  expect(reply.textContent).not.toContain('https://')
  expect(reply.textContent).not.toContain('wrong label')
  link.click()
  expect(navigate).toHaveBeenCalledWith(`/documents/pm.receivable_payment/${id}`)
})

test('failed turns show a retry action and never look complete', async () => {
  await page.viewport(1440, 900)
  vi.mocked(streamAssistant).mockRejectedValueOnce(new Error('Connection ended'))
  const { view } = await mountShell()
  await view.getByRole('textbox', { name: 'Ask a question' }).fill('Question')
  await view.getByRole('button', { name: 'Send', exact: true }).click()
  await expect.element(view.getByRole('alert')).toHaveTextContent('Connection ended')

  await view.getByRole('button', { name: 'Retry', exact: true }).click()

  await expect.element(view.getByText('Seven', { exact: true })).toBeVisible()
  await expect.element(view.getByRole('link', { name: 'Aging report' })).not.toBeInTheDocument()
  expect(streamAssistant).toHaveBeenCalledTimes(2)
})

test('uses host page semantics and editor routing for Assistant sources on custom routes', async () => {
  await page.viewport(1440, 900)
  const firstId = '11111111-1111-4111-8111-111111111111'
  const secondId = '22222222-2222-4222-8222-222222222222'
  configureEditorRouting({ buildCatalogFullPageUrl: (_, id) => `/customers/${id}` })
  configureNgbNavigation({
    resolvePageContext: route => route.name === 'customer'
      ? { kind: NgbSourceKinds.Catalog, typeCode: 'example.customer', id: String(route.params.customerId) }
      : undefined,
  })
  vi.mocked(streamAssistant).mockImplementation(async (request, event) => {
    const source = { kind: NgbSourceKinds.Catalog, typeCode: 'example.customer', id: request.context!.id!, title: 'Customer Display' }
    event({ type: 'source', source })
    event({ type: 'text', text: `[Record](ngb-source:catalog:example.customer:${source.id})` })
    event({ type: 'done' })
  })
  const { view, navigate, router } = await mountShell(`/customers/${firstId}`)
  await view.getByRole('textbox', { name: 'Ask a question' }).fill('Show this customer')
  await view.getByRole('button', { name: 'Send', exact: true }).click()

  expect(vi.mocked(streamAssistant).mock.calls[0]![0].context)
    .toEqual({ kind: 'catalog', typeCode: 'example.customer', id: firstId })
  const link = document.querySelector<HTMLAnchorElement>('.assistant-markdown a')!
  expect(link.textContent).toBe('Customer Display')
  expect(link.getAttribute('href')).toBe(`/customers/${firstId}`)
  link.click()
  expect(navigate).toHaveBeenCalledWith(`/customers/${firstId}`)

  await router.push(`/customers/${secondId}`)
  await view.getByRole('textbox', { name: 'Ask a question' }).fill('Show the next customer')
  await view.getByRole('button', { name: 'Send', exact: true }).click()
  expect(vi.mocked(streamAssistant).mock.calls[1]![0].context)
    .toEqual({ kind: 'catalog', typeCode: 'example.customer', id: secondId })
})

test('updates compact editor context when only the selected record query changes', async () => {
  await page.viewport(1440, 900)
  const firstId = '11111111-1111-4111-8111-111111111111'
  const secondId = '22222222-2222-4222-8222-222222222222'
  const { view, router } = await mountShell(`/catalogs/example.customer?panel=edit&id=${firstId}`)
  await view.getByRole('textbox', { name: 'Ask a question' }).fill('Show this record')
  await view.getByRole('button', { name: 'Send', exact: true }).click()
  expect(vi.mocked(streamAssistant).mock.calls[0]![0].context)
    .toEqual({ kind: 'catalog', typeCode: 'example.customer', id: firstId })

  await router.push({ query: { panel: 'edit', id: secondId } })
  await view.getByRole('textbox', { name: 'Ask a question' }).fill('Show the next record')
  await view.getByRole('button', { name: 'Send', exact: true }).click()
  expect(vi.mocked(streamAssistant).mock.calls[1]![0].context)
    .toEqual({ kind: 'catalog', typeCode: 'example.customer', id: secondId })
})

test('does not fall back to a URL scope when the host explicitly disables page context', async () => {
  await page.viewport(1440, 900)
  configureNgbNavigation({ resolvePageContext: () => null })
  const { view } = await mountShell('/reports/example.summary')
  await expect.element(view.getByRole('checkbox')).not.toBeInTheDocument()
  await view.getByRole('textbox', { name: 'Ask a question' }).fill('What reports can I run?')
  await view.getByRole('button', { name: 'Send', exact: true }).click()

  expect(vi.mocked(streamAssistant).mock.calls[0]![0].context).toBeNull()
})
