import '@ngbplatform/ui/styles'
import { createPinia } from 'pinia'
import { afterEach, expect, test, vi } from 'vitest'
import { page } from 'vitest/browser'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, type Component } from 'vue'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'
import {
  configureNgbCommandPalette,
  configureNgbEditor,
  configureNgbMetadata,
  NgbMetadataCatalogListPage,
  provideToasts,
  type CatalogTypeMetadata,
} from '@ngbplatform/ui'

const id = '019f34de-e2d7-757f-9e45-6204905696a6'

async function renderCatalog({ typeCode, editor }: { typeCode: string; editor: Component }) {
  await page.viewport(1440, 900)
  let item = { id, display: 'Test record', payload: { fields: { display: 'Test record' } }, isMarkedForDeletion: false }
  const recordPath = `/api/catalogs/${typeCode}/${id}`
  const writes: { method: string; path: string; body: unknown }[] = []
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
    const url = new URL(input instanceof Request ? input.url : String(input))
    const method = init?.method ?? 'GET'
    const path = url.pathname
    if (method === 'GET' && path === recordPath) return Response.json(item)
    const body = init?.body ? JSON.parse(String(init.body)) : undefined
    writes.push({ method, path, body })
    if (method === 'PUT' && path === recordPath) {
      item = { ...item, display: body.fields.display, payload: body }
      return Response.json(item)
    }
    if (method === 'POST' && path === `${recordPath}/mark-for-deletion`) {
      item.isMarkedForDeletion = true
      return new Response(null, { status: 204 })
    }
    if (method === 'POST' && path === `${recordPath}/unmark-for-deletion`) {
      item.isMarkedForDeletion = false
      return new Response(null, { status: 204 })
    }
    throw new Error(`Unexpected request: ${method} ${path}`)
  })
  const metadata: CatalogTypeMetadata = {
    catalogType: typeCode,
    displayName: 'Test catalog',
    kind: 1,
    list: { columns: [{ key: 'display', label: 'Display', dataType: 'String', isSortable: true, align: 1 }] },
    form: { sections: [{ title: 'Main', rows: [{ fields: [
      { key: 'display', label: 'Display', dataType: 'String', uiControl: 1, isRequired: true, isReadOnly: false },
    ] }] }] },
    parts: null,
  }
  configureNgbMetadata({
    loadCatalogTypeMetadata: async () => metadata,
    loadDocumentTypeMetadata: vi.fn(),
  })
  const loadAudit = vi.fn().mockResolvedValue({ items: [], limit: 50, nextCursor: null })
  configureNgbEditor({
    loadDocumentById: vi.fn(),
    loadDocumentEffects: vi.fn(),
    loadDocumentGraph: vi.fn(),
    loadEntityAuditLog: loadAudit,
    documentActions: { loadEditorState: vi.fn(), execute: vi.fn() },
  })
  const clipboard = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue(undefined)
  const loadPage = vi.fn(async () => ({ items: [structuredClone(item)], total: 1 }))
  const listPath = `/catalogs/${typeCode}`
  const drawerPath = `${listPath}?panel=edit&id=${id}`
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/catalogs/:catalogType', component: NgbMetadataCatalogListPage, props: { editorComponent: editor, loadPage } },
      { path: '/catalogs/:catalogType/:id', component: { render: () => h('div', 'Full page') } },
    ],
  })
  configureNgbCommandPalette({ router, recentStorageKey: 'ngb:test:catalog-actions', loadReportItems: async () => [] })
  await router.push(drawerPath)
  await router.isReady()
  const view = await render(defineComponent({
    setup() {
      provideToasts()
      return () => h(RouterView)
    },
  }), { global: { plugins: [createPinia(), router] } })
  await expect.element(view.getByRole('textbox')).toHaveValue('Test record')
  await expect.element(view.getByTitle('Save', { exact: true })).toBeEnabled()
  return { view, router, clipboard, loadAudit, loadPage, listPath, drawerPath, writes, recordPath }
}

export function testCatalogDrawerActions(editor: Component, typeCodes: string[]) {
  afterEach(() => vi.restoreAllMocks())

  test.each(typeCodes)('%s: catalog toolbar shares, audits, saves, marks, restores and expands', async (typeCode) => {
    const { view, router, clipboard, loadAudit, loadPage, listPath, drawerPath, writes, recordPath } = await renderCatalog({ editor, typeCode })

    await view.getByTitle('Share link', { exact: true }).click()
    await expect.poll(() => clipboard.mock.calls.length).toBe(1)
    expect(clipboard).toHaveBeenCalledWith(new URL(drawerPath, window.location.origin).href)

    await view.getByTitle('Audit log', { exact: true }).click()
    await expect.element(view.getByText('No history yet.', { exact: true })).toBeVisible()
    expect(loadAudit).toHaveBeenCalledWith(2, id, expect.any(Object))
    await view.getByTitle('Close', { exact: true }).last().click()

    await view.getByRole('textbox').fill('Updated record')
    await view.getByTitle('Save', { exact: true }).click()
    await expect.poll(() => router.currentRoute.value.fullPath).toBe(listPath)
    expect(writes).toEqual([{ method: 'PUT', path: recordPath, body: { fields: { display: 'Updated record' } } }])
    expect(loadPage).toHaveBeenCalledTimes(2)

    await router.push(drawerPath)
    await expect.element(view.getByRole('textbox')).toHaveValue('Updated record')
    await view.getByTitle('Mark for deletion', { exact: true }).click()
    await expect.element(view.getByText('Mark for deletion?', { exact: true })).toBeVisible()
    expect(writes.filter(({ method }) => method === 'POST')).toEqual([])
    await view.getByRole('button', { name: 'Cancel', exact: true }).click()
    expect(writes.filter(({ method }) => method === 'POST')).toEqual([])
    await view.getByTitle('Mark for deletion', { exact: true }).click()
    await view.getByRole('button', { name: 'Mark', exact: true }).click()
    await expect.poll(() => router.currentRoute.value.fullPath).toBe(listPath)
    expect(writes).toContainEqual({ method: 'POST', path: `${recordPath}/mark-for-deletion`, body: undefined })

    await router.push(drawerPath)
    await expect.element(view.getByTitle('Unmark for deletion', { exact: true })).toBeEnabled()
    await expect.element(view.getByTitle('Save', { exact: true })).toBeDisabled()
    await view.getByTitle('Unmark for deletion', { exact: true }).click()
    await expect.poll(() => router.currentRoute.value.fullPath).toBe(listPath)
    expect(writes).toContainEqual({ method: 'POST', path: `${recordPath}/unmark-for-deletion`, body: undefined })

    await router.push(drawerPath)
    await expect.element(view.getByRole('textbox')).toHaveValue('Updated record')
    await view.getByTitle('Open full page', { exact: true }).click()
    await expect.poll(() => router.currentRoute.value.path).toBe(`${listPath}/${id}`)
    expect(atob(String(router.currentRoute.value.query.back))).toBe(drawerPath)
  })
}
