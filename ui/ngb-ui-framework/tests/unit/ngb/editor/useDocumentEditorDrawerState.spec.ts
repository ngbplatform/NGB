import { computed, nextTick, reactive, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Router } from 'vue-router'

const readDocumentCopyDraftMock = vi.hoisted(() => vi.fn())
const replaceCleanRouteQueryMock = vi.hoisted(() => vi.fn().mockResolvedValue(undefined))

vi.mock('../../../../src/ngb/editor/documentCopyDraft', () => ({
  readDocumentCopyDraft: readDocumentCopyDraftMock,
}))

vi.mock('../../../../src/ngb/router/queryParams', async () => {
  const actual = await vi.importActual('../../../../src/ngb/router/queryParams')
  return {
    ...actual,
    replaceCleanRouteQuery: replaceCleanRouteQueryMock,
  }
})

import {
  isDocumentEditorDrawerQueryKey,
  useDocumentEditorDrawerState,
} from '../../../../src/ngb/editor/useDocumentEditorDrawerState'

function createHarness(initialQuery: Record<string, unknown> = {}) {
  const route = reactive({
    path: '/documents/pm.invoice',
    query: { ...initialQuery } as Record<string, unknown>,
  })
  const router = {
    replace: vi.fn(),
    push: vi.fn(),
  } as unknown as Router
  const documentType = ref('pm.invoice')

  const drawer = useDocumentEditorDrawerState({
    route: route as never,
    router,
    documentType: computed(() => documentType.value),
  })

  return {
    route,
    router,
    documentType,
    drawer,
  }
}

describe('document editor drawer state', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('recognizes reserved drawer query keys', () => {
    expect(isDocumentEditorDrawerQueryKey('panel')).toBe(true)
    expect(isDocumentEditorDrawerQueryKey('id')).toBe(true)
    expect(isDocumentEditorDrawerQueryKey('copyDraft')).toBe(true)
    expect(isDocumentEditorDrawerQueryKey('status')).toBe(false)
  })

  it('opens a new drawer from route query, resolves copy drafts, and clears query keys', async () => {
    readDocumentCopyDraftMock.mockReturnValue({
      documentType: 'pm.invoice',
      fields: {
        customer_id: 'customer-1',
      },
      parts: {
        lines: {
          rows: [{ amount: 1250 }],
        },
      },
    })

    const { route, router, drawer } = createHarness({
      panel: 'new',
      copyDraft: 'copy-token',
    })
    await nextTick()

    expect(drawer.drawerMode.value).toBe('new')
    expect(drawer.isPanelOpen.value).toBe(true)
    expect(drawer.currentEditorId.value).toBeNull()
    expect(drawer.initialFields.value).toEqual({
      customer_id: 'customer-1',
    })
    expect(drawer.initialParts.value).toEqual({
      lines: {
        rows: [{ amount: 1250 }],
      },
    })
    expect(drawer.expandTo.value).toBe('/documents/pm.invoice/new')
    expect(readDocumentCopyDraftMock).toHaveBeenCalledWith('copy-token', 'pm.invoice')
    expect(replaceCleanRouteQueryMock).toHaveBeenCalledWith(route, router, {
      panel: undefined,
      id: undefined,
      copyDraft: undefined,
    })
  })

  it('opens edit mode, reopens created documents, and clears route query on close', async () => {
    const { route, router, drawer } = createHarness({
      panel: 'edit',
      id: 'doc-1',
    })
    await nextTick()

    expect(drawer.drawerMode.value).toBe('edit')
    expect(drawer.currentEditorId.value).toBe('doc-1')
    expect(drawer.expandTo.value).toBe('/documents/pm.invoice/doc-1')

    drawer.reopenCreatedDocument('doc-2')
    expect(drawer.currentEditorId.value).toBe('doc-2')

    await drawer.closeDrawer()
    expect(drawer.drawerMode.value).toBe('closed')
    expect(replaceCleanRouteQueryMock).toHaveBeenCalledWith(route, router, {
      panel: undefined,
      id: undefined,
      copyDraft: undefined,
    })
  })

  it('resets local drawer state when the document type changes without drawer query params', async () => {
    const { documentType, drawer } = createHarness()

    drawer.openEditDrawer('doc-9')
    expect(drawer.drawerMode.value).toBe('edit')

    documentType.value = 'pm.credit_note'
    await nextTick()

    expect(drawer.drawerMode.value).toBe('closed')
    expect(drawer.currentEditorId.value).toBeNull()
  })
})
