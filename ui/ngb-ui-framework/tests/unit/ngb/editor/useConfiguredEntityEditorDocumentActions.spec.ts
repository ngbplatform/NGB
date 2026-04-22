import { computed, ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'

import type { DocumentDerivationActionDto } from '../../../../src/ngb/api/contracts'
import { useConfiguredEntityEditorDocumentActions } from '../../../../src/ngb/editor/useConfiguredEntityEditorDocumentActions'

const resolveNgbEditorDocumentActionsMock = vi.hoisted(() => vi.fn())
const deriveDocumentMock = vi.hoisted(() => vi.fn())
const getDocumentDerivationActionsMock = vi.hoisted(() => vi.fn())

vi.mock('../../../../src/ngb/editor/config', async () => {
  const actual = await vi.importActual('../../../../src/ngb/editor/config')
  return {
    ...actual,
    resolveNgbEditorDocumentActions: resolveNgbEditorDocumentActionsMock,
  }
})

vi.mock('../../../../src/ngb/api/documents', () => {
  return {
    deriveDocument: deriveDocumentMock,
    getDocumentDerivationActions: getDocumentDerivationActionsMock,
  }
})

function createArgs() {
  const kind = ref<'catalog' | 'document'>('document')
  const typeCode = ref('pm.invoice')
  const currentId = ref<string | null>('doc-1')
  const model = ref({
    customer_id: 'customer-1',
  })
  const uiEffects = ref({
    isPosted: false,
    canEdit: true,
    canPost: true,
    canUnpost: false,
    canRepost: false,
    canApply: false,
  })
  const loading = ref(false)
  const saving = ref(false)
  const requestNavigate = vi.fn()
  const setEditorError = vi.fn()
  const normalizeEditorError = vi.fn((cause: unknown) => ({
    summary: cause instanceof Error ? cause.message : 'normalized',
    issues: [],
  }))
  const metadataStore = {
    ensureDocumentType: vi.fn(async (documentType: string) => ({
      documentType,
      displayName: documentType === 'ab.credit_note' ? 'Credit Note' : 'Sales Invoice',
      kind: 2,
      form: null,
      list: null,
      parts: null,
      actions: null,
      presentation: null,
      capabilities: null,
    })),
  }
  const loadDerivationActions = vi
    .fn<(documentType: string, id: string) => Promise<DocumentDerivationActionDto[]>>()
    .mockResolvedValue([])

  return {
    state: {
      kind,
      typeCode,
      currentId,
      model,
      uiEffects,
      loading,
      saving,
    },
    requestNavigate,
    setEditorError,
    normalizeEditorError,
    metadataStore,
    loadDerivationActions,
    args: {
      kind: computed(() => kind.value),
      typeCode: computed(() => typeCode.value),
      currentId: computed(() => currentId.value),
      model,
      uiEffects: computed(() => uiEffects.value),
      loading: computed(() => loading.value),
      saving: computed(() => saving.value),
      requestNavigate,
      metadataStore,
      setEditorError,
      normalizeEditorError,
      loadDerivationActions,
    },
  }
}

describe('configured entity editor document actions', () => {
  async function flushAsyncWork() {
    await new Promise((resolve) => setTimeout(resolve, 0))
  }

  it('projects configured actions into primary and grouped buckets and dispatches matches', () => {
    const approveRun = vi.fn()
    const emailRun = vi.fn()
    const printRun = vi.fn()

    resolveNgbEditorDocumentActionsMock.mockReturnValueOnce([
      {
        item: { key: 'approve', title: 'Approve', icon: 'check' },
        run: approveRun,
      },
      {
        item: { key: 'email', title: 'Email', icon: 'mail' },
        group: { key: 'output', label: 'Output' },
        run: emailRun,
      },
      {
        item: { key: 'printPacket', title: 'Print packet', icon: 'printer' },
        group: { key: 'output', label: 'Output' },
        run: printRun,
      },
    ])

    const { args, requestNavigate } = createArgs()
    const actions = useConfiguredEntityEditorDocumentActions(args)
    expect(actions.extraPrimaryActions.value).toEqual([
      { key: 'approve', title: 'Approve', icon: 'check' },
    ])

    expect(resolveNgbEditorDocumentActionsMock).toHaveBeenCalledWith({
      context: {
        kind: 'document',
        typeCode: 'pm.invoice',
      },
      documentId: 'doc-1',
      model: {
        customer_id: 'customer-1',
      },
      uiEffects: {
        isPosted: false,
        canEdit: true,
        canPost: true,
        canUnpost: false,
        canRepost: false,
        canApply: false,
      },
      loading: false,
      saving: false,
      navigate: requestNavigate,
    })
    expect(actions.extraMoreActionGroups.value).toEqual([
      {
        key: 'output',
        label: 'Output',
        items: [
          { key: 'email', title: 'Email', icon: 'mail' },
          { key: 'printPacket', title: 'Print packet', icon: 'printer' },
        ],
      },
    ])

    expect(actions.handleConfiguredAction('email')).toBe(true)
    expect(emailRun).toHaveBeenCalledTimes(1)
    expect(actions.handleConfiguredAction('missing')).toBe(false)
  })

  it('loads derive actions into the create group sorted by target document display name and navigates to the created draft', async () => {
    resolveNgbEditorDocumentActionsMock.mockReturnValueOnce([])
    deriveDocumentMock.mockResolvedValueOnce({
      id: 'derived-1',
      display: 'Sales Invoice SI-001',
      payload: { fields: {} },
      status: 1,
      isMarkedForDeletion: false,
    })

    const { args, requestNavigate, metadataStore, loadDerivationActions } = createArgs()
    loadDerivationActions.mockResolvedValueOnce([
      {
        code: 'derive-sales-invoice',
        name: 'Generate Invoice Draft',
        fromTypeCode: 'ab.timesheet',
        toTypeCode: 'ab.sales_invoice',
        relationshipCodes: ['created_from'],
      },
      {
        code: 'derive-credit-note',
        name: 'Create Credit Note',
        fromTypeCode: 'ab.timesheet',
        toTypeCode: 'ab.credit_note',
        relationshipCodes: ['based_on'],
      },
    ])

    const actions = useConfiguredEntityEditorDocumentActions(args)
    await flushAsyncWork()

    expect(metadataStore.ensureDocumentType).toHaveBeenCalledWith('ab.sales_invoice')
    expect(metadataStore.ensureDocumentType).toHaveBeenCalledWith('ab.credit_note')
    expect(actions.extraMoreActionGroups.value).toEqual([
      {
        key: 'create',
        label: 'Create',
        items: [
          { key: 'derive:derive-credit-note', title: 'Credit Note', icon: 'file-text', disabled: false },
          { key: 'derive:derive-sales-invoice', title: 'Sales Invoice', icon: 'file-text', disabled: false },
        ],
      },
    ])

    expect(actions.handleConfiguredAction('derive:derive-sales-invoice')).toBe(true)
    await flushAsyncWork()

    expect(deriveDocumentMock).toHaveBeenCalledWith('ab.sales_invoice', {
      sourceDocumentId: 'doc-1',
      relationshipType: 'created_from',
    })
    expect(requestNavigate).toHaveBeenCalledWith('/documents/ab.sales_invoice/derived-1')
  })

  it('normalizes derive-action failures into editor errors instead of leaking unhandled promise rejections', async () => {
    const apiError = new Error('An invoice draft or posted invoice already exists for this timesheet.')

    resolveNgbEditorDocumentActionsMock.mockReturnValueOnce([])
    deriveDocumentMock.mockRejectedValueOnce(apiError)

    const { args, setEditorError, normalizeEditorError, loadDerivationActions } = createArgs()
    loadDerivationActions.mockResolvedValueOnce([
      {
        code: 'derive-sales-invoice',
        name: 'Generate Invoice Draft',
        fromTypeCode: 'ab.timesheet',
        toTypeCode: 'ab.sales_invoice',
        relationshipCodes: ['created_from'],
      },
    ])

    const actions = useConfiguredEntityEditorDocumentActions(args)
    await flushAsyncWork()

    expect(actions.handleConfiguredAction('derive:derive-sales-invoice')).toBe(true)
    await flushAsyncWork()

    expect(setEditorError).toHaveBeenNthCalledWith(1, null)
    expect(normalizeEditorError).toHaveBeenCalledWith(apiError)
    expect(setEditorError).toHaveBeenNthCalledWith(2, {
      summary: apiError.message,
      issues: [],
    })
  })

  it('returns no configured actions for non-documents or missing entity ids', () => {
    resolveNgbEditorDocumentActionsMock.mockClear()

    const { args, state } = createArgs()
    state.kind.value = 'catalog'

    const catalogActions = useConfiguredEntityEditorDocumentActions(args)
    expect(catalogActions.extraPrimaryActions.value).toEqual([])
    expect(catalogActions.extraMoreActionGroups.value).toEqual([])
    expect(resolveNgbEditorDocumentActionsMock).not.toHaveBeenCalled()

    state.kind.value = 'document'
    state.currentId.value = null

    const missingIdActions = useConfiguredEntityEditorDocumentActions(args)
    expect(missingIdActions.extraPrimaryActions.value).toEqual([])
    expect(missingIdActions.extraMoreActionGroups.value).toEqual([])
    expect(resolveNgbEditorDocumentActionsMock).not.toHaveBeenCalled()
  })
})
