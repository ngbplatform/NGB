import { createPinia } from 'pinia'
import { afterEach, expect, test, vi } from 'vitest'
import { page } from 'vitest/browser'
import { render } from 'vitest-browser-vue'
import { defineComponent, h } from 'vue'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'
import {
  configureNgbCommandPalette,
  configureNgbEditor,
  configureNgbMetadata,
  NgbMetadataCatalogListPage,
  provideToasts,
  type CatalogTypeMetadata,
  type RecordFields,
} from '@ngbplatform/ui'

vi.mock('keycloak-js', () => ({
  default: class {
    authenticated = false
    async init() { return false }
  },
}))

import TradeEntityEditor from '../../../src/editor/TradeEntityEditor.vue'

afterEach(() => vi.restoreAllMocks())

test('Party drawer explains missing roles, retains edits and saves after selecting a role', async () => {
  await page.viewport(1440, 900)
  const id = '019e2eca-d808-79d2-9ee2-5c076ca5652f'
  const recordPath = `/api/catalogs/trd.party/${id}`
  const message = 'Select at least one role: Customer or Vendor.'
  let fields: RecordFields = {
    display: 'Atlas Industrial Supply', name: 'Atlas Industrial Supply',
    is_customer: true, is_vendor: false, is_active: true,
  }
  const item = () => ({ id, display: fields.display, payload: { fields }, isMarkedForDeletion: false })
  const writes: RecordFields[] = []
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
    const url = new URL(input instanceof Request ? input.url : String(input))
    const method = init?.method ?? 'GET'
    if (url.pathname === recordPath && method === 'GET') return Response.json(item())
    if (url.pathname === recordPath && method === 'PUT') {
      const body = JSON.parse(String(init!.body)) as { fields: RecordFields }
      writes.push(body.fields)
      if (!body.fields.is_customer && !body.fields.is_vendor) {
        return Response.json({
          status: 400, title: 'Validation failed', detail: message,
          error: {
            code: 'trd.validation.party.role_required', kind: 'Validation',
            errors: { is_customer: [message], is_vendor: [message] },
            issues: ['is_customer', 'is_vendor'].map(path => ({ path, message, scope: 'field' })),
          },
        }, { status: 400, headers: { 'Content-Type': 'application/problem+json' } })
      }
      fields = body.fields
      return Response.json(item())
    }
    throw new Error(`Unexpected request: ${method} ${url.pathname}`)
  })
  const metadata: CatalogTypeMetadata = {
    catalogType: 'trd.party', displayName: 'Party', kind: 1, parts: null,
    list: { columns: [{ key: 'display', label: 'Display', dataType: 'String', isSortable: true, align: 1 }] },
    form: { sections: [{ title: 'Main', rows: [{ fields: [
      { key: 'display', label: 'Display', dataType: 'String', uiControl: 1, isRequired: true, isReadOnly: false },
      { key: 'name', label: 'Name', dataType: 'String', uiControl: 1, isRequired: true, isReadOnly: false },
      ...[['is_customer', 'Is Customer'], ['is_vendor', 'Is Vendor'], ['is_active', 'Is Active']].map(([key, label]) => ({
        key: key!, label: label!, dataType: 'Boolean', uiControl: 5, isRequired: true, isReadOnly: false,
      })),
    ] }] }] },
  }
  configureNgbMetadata({ loadCatalogTypeMetadata: async () => metadata, loadDocumentTypeMetadata: vi.fn() })
  configureNgbEditor({
    loadDocumentById: vi.fn(), loadDocumentEffects: vi.fn(), loadDocumentGraph: vi.fn(), loadEntityAuditLog: vi.fn(),
    documentActions: { loadEditorState: vi.fn(), execute: vi.fn() },
  })
  const listPath = '/catalogs/trd.party'
  const drawerPath = `${listPath}?panel=edit&id=${id}`
  const loadPage = vi.fn(async () => ({ items: [structuredClone(item())], total: 1 }))
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{
      path: '/catalogs/:catalogType', component: NgbMetadataCatalogListPage,
      props: { editorComponent: TradeEntityEditor, loadPage },
    }],
  })
  configureNgbCommandPalette({ router, recentStorageKey: 'ngb:test:party-roles', loadReportItems: async () => [] })
  await router.push(drawerPath)
  await router.isReady()
  const view = await render(defineComponent({
    setup() { provideToasts(); return () => h(RouterView) },
  }), { global: { plugins: [createPinia(), router] } })

  const customer = view.getByRole('checkbox').nth(0)
  const vendor = view.getByRole('checkbox').nth(1)
  await expect.element(customer).toBeChecked()
  await view.getByRole('textbox').first().fill('Atlas updated')
  await page.elementLocator(document.querySelector('[data-validation-key="is_customer"] label')!).click()
  await expect.element(customer).not.toBeChecked()
  await view.getByTitle('Save', { exact: true }).click()

  await expect.element(view.getByText(message, { exact: true }).first()).toBeVisible()
  expect(router.currentRoute.value.fullPath).toBe(drawerPath)
  expect(writes).toHaveLength(1)
  expect(writes[0]).toMatchObject({ display: 'Atlas updated', is_customer: false, is_vendor: false })
  for (const field of ['is_customer', 'is_vendor']) {
    await expect.poll(() => document.querySelector(`[data-validation-key="${field}"]`)?.textContent).toContain(message)
  }
  await expect.element(view.getByRole('textbox').first()).toHaveValue('Atlas updated')
  await expect.element(view.getByText('Something went wrong.', { exact: true })).not.toBeInTheDocument()

  await page.elementLocator(document.querySelector('[data-validation-key="is_vendor"] label')!).click()
  await expect.element(vendor).toBeChecked()
  await view.getByTitle('Save', { exact: true }).click()
  await expect.poll(() => router.currentRoute.value.fullPath).toBe(listPath)
  expect(writes).toHaveLength(2)
  expect(fields).toMatchObject({ display: 'Atlas updated', is_customer: false, is_vendor: true })
  expect(loadPage).toHaveBeenCalledTimes(2)
})
