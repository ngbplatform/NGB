import { ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  applyInitialFieldValues: vi.fn(),
  buildCatalogFullPageUrl: vi.fn(() => '/catalog/full-page'),
  buildDocumentFullPageUrl: vi.fn(() => '/document/full-page'),
  buildFieldsPayload: vi.fn(() => ({ memo: 'payload' })),
  createCatalog: vi.fn(),
  createDraft: vi.fn(),
  deleteCatalog: vi.fn(),
  ensureModelKeys: vi.fn(),
  getCatalogById: vi.fn(),
  getDocumentEditorState: vi.fn(),
  getDocumentEffects: vi.fn(),
  hydrateEntityReferenceFieldsForEditing: vi.fn(),
  markCatalogForDeletion: vi.fn(),
  markDocumentForDeletion: vi.fn(),
  postDocument: vi.fn(),
  resolveNavigateOnCreate: vi.fn(() => false),
  sanitizeNgbEditorModelForEditing: vi.fn(),
  syncNgbEditorComputedDisplay: vi.fn(),
  unmarkCatalogForDeletion: vi.fn(),
  unmarkDocumentForDeletion: vi.fn(),
  unpostDocument: vi.fn(),
  updateCatalog: vi.fn(),
  updateDraft: vi.fn(),
}))

vi.mock('@ngbplatform/ui', () => ({
  ...mocks,
  setModelFromFields: (model: { value: unknown }, fields: unknown) => { model.value = fields },
}))

import { useCatalogEntityEditorPersistence } from '../../../src/editor/pm/useCatalogEntityEditorPersistence'
import { useDocumentEntityEditorPersistence } from '../../../src/editor/pm/useDocumentEntityEditorPersistence'

function context(overrides: Record<string, unknown> = {}) {
  return {
    typeCode: ref('pm.receivable_payment'),
    currentId: ref('doc-1'),
    isNew: ref(false),
    mode: ref('drawer'),
    navigateOnCreate: ref<boolean | null>(null),
    catalogMeta: ref<unknown>(null),
    catalogItem: ref<unknown>(null),
    docMeta: ref<unknown>(null),
    metadata: ref({ form: { sections: [] } }),
    doc: ref<unknown>(null),
    docEffects: ref<unknown>(null),
    model: ref<Record<string, unknown>>({}),
    initialFields: ref<unknown>(null),
    initialParts: ref<unknown>(null),
    metaStore: {
      ensureDocumentType: vi.fn().mockResolvedValue({ form: {} }),
      ensureCatalogType: vi.fn().mockResolvedValue({ form: {} }),
    },
    lookupStore: {},
    currentEditorContext: vi.fn(() => ({ context: true })),
    resetInitialSnapshot: vi.fn(),
    emitCreated: vi.fn(),
    emitSaved: vi.fn(),
    router: { replace: vi.fn() },
    toasts: [] as unknown[],
    setEditorError: vi.fn(),
    leaseEditor: {
      isLeaseDocument: ref(false),
      applyInitialParts: vi.fn(),
      applyPersistedParts: vi.fn(),
      buildSaveParts: vi.fn(() => ({ parties: { rows: [] } })),
      ensureLeasePartiesInitialized: vi.fn(),
      validateLeasePartiesBeforeSave: vi.fn(() => null as string | null),
    },
    ...overrides,
  }
}

describe('property-management document editor persistence', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.buildFieldsPayload.mockReturnValue({ memo: 'payload' })
    mocks.resolveNavigateOnCreate.mockReturnValue(false)
  })

  it('loads effects and handles an unavailable effects endpoint', async () => {
    const args = context()
    const adapter = useDocumentEntityEditorPersistence(args as never)
    mocks.getDocumentEffects.mockResolvedValueOnce({ entries: [1] })
    await adapter.loadEffectsSnapshot('pm.receivable_payment', 'doc-1')
    expect(args.docEffects.value).toEqual({ entries: [1] })
    mocks.getDocumentEffects.mockRejectedValueOnce(new Error('offline'))
    await adapter.loadEffectsSnapshot('pm.receivable_payment', 'doc-1')
    expect(args.docEffects.value).toBeNull()
  })

  it('initializes a new document including initial fields and parts', async () => {
    const args = context({
      isNew: ref(true),
      initialFields: ref({ memo: 'initial' }),
      initialParts: ref({ parties: { rows: [] } }),
    })
    await useDocumentEntityEditorPersistence(args as never).load()
    expect(args.doc.value).toBeNull()
    expect(args.docEffects.value).toBeNull()
    expect(mocks.applyInitialFieldValues).toHaveBeenCalledWith(args.model.value, { memo: 'initial' })
    expect(args.leaseEditor.applyInitialParts).toHaveBeenCalledWith({ parties: { rows: [] } })
    expect(args.resetInitialSnapshot).toHaveBeenCalledOnce()
  })

  it('loads and hydrates an existing document', async () => {
    const document = { id: 'doc-1', payload: { fields: { memo: 'loaded' }, parts: { parties: [] } } }
    mocks.getDocumentEditorState.mockResolvedValueOnce({ document, documentVersion: 2, actions: [] })
    const args = context()
    await useDocumentEntityEditorPersistence(args as never).load()
    expect(args.doc.value).toEqual(document)
    expect(args.model.value).toEqual({ memo: 'loaded' })
    expect(args.leaseEditor.applyPersistedParts).toHaveBeenCalledWith({ parties: [] })
  })

  it('blocks lease save and exposes a structured tenant validation error', async () => {
    const leaseEditor = {
      isLeaseDocument: ref(true),
      applyInitialParts: vi.fn(),
      applyPersistedParts: vi.fn(),
      buildSaveParts: vi.fn(() => ({ parties: { rows: [] } })),
      ensureLeasePartiesInitialized: vi.fn(),
      validateLeasePartiesBeforeSave: vi.fn(() => 'Exactly one primary tenant is required.'),
    }
    const args = context({ isNew: ref(true), leaseEditor })
    await useDocumentEntityEditorPersistence(args as never).save()
    expect(leaseEditor.ensureLeasePartiesInitialized).toHaveBeenCalledOnce()
    expect(args.setEditorError).toHaveBeenCalledWith(expect.objectContaining({
      summary: 'Tenant list is invalid.',
      status: 400,
    }))
    expect(mocks.createDraft).not.toHaveBeenCalled()
  })

  it('rebuilds lease parts after successful lease validation', async () => {
    const leaseEditor = {
      isLeaseDocument: ref(true),
      applyInitialParts: vi.fn(),
      applyPersistedParts: vi.fn(),
      buildSaveParts: vi.fn()
        .mockReturnValueOnce({ parties: { rows: ['before'] } })
        .mockReturnValueOnce({ parties: { rows: ['normalized'] } }),
      ensureLeasePartiesInitialized: vi.fn(),
      validateLeasePartiesBeforeSave: vi.fn(() => null),
    }
    mocks.createDraft.mockResolvedValueOnce({ id: 'created-lease', payload: { parts: { parties: { rows: ['server'] } } } })
    const args = context({ isNew: ref(true), leaseEditor })
    await useDocumentEntityEditorPersistence(args as never).save()
    expect(mocks.createDraft).toHaveBeenCalledWith('pm.receivable_payment', {
      fields: { memo: 'payload' },
      parts: { parties: { rows: ['normalized'] } },
    })
  })

  it('creates a new draft and navigates when requested', async () => {
    mocks.resolveNavigateOnCreate.mockReturnValueOnce(true)
    mocks.createDraft.mockResolvedValueOnce({ id: 'created-1', payload: { parts: { parties: ['server'] } } })
    const args = context({ isNew: ref(true) })
    await useDocumentEntityEditorPersistence(args as never).save()
    expect(args.currentId.value).toBe('created-1')
    expect(args.emitCreated).toHaveBeenCalledWith('created-1')
    expect(args.router.replace).toHaveBeenCalledWith('/document/full-page')
  })

  it('creates a draft without navigation and accepts missing response parts', async () => {
    mocks.createDraft.mockResolvedValueOnce({ id: 'created-2', payload: {} })
    const args = context({ isNew: ref(true) })
    await useDocumentEntityEditorPersistence(args as never).save()
    expect(args.leaseEditor.applyPersistedParts).toHaveBeenCalledWith(undefined)
    expect(args.router.replace).not.toHaveBeenCalled()
  })

  it('updates and fully rehydrates an existing draft', async () => {
    mocks.updateDraft.mockResolvedValueOnce({ id: 'doc-1', payload: { fields: { memo: 'updated' }, parts: { parties: ['updated'] } } })
    const args = context()
    await useDocumentEntityEditorPersistence(args as never).save()
    expect(args.model.value).toEqual({ memo: 'updated' })
    expect(args.leaseEditor.applyPersistedParts).toHaveBeenCalledWith({ parties: ['updated'] })
    expect(args.emitSaved).toHaveBeenCalledOnce()
  })

  it('executes every document lifecycle mutation', async () => {
    mocks.markDocumentForDeletion.mockResolvedValueOnce({ state: 'marked' })
    mocks.unmarkDocumentForDeletion.mockResolvedValueOnce({ state: 'restored' })
    mocks.postDocument.mockResolvedValueOnce({ state: 'posted' })
    mocks.unpostDocument.mockResolvedValueOnce({ state: 'draft' })
    const args = context()
    const adapter = useDocumentEntityEditorPersistence(args as never)
    await adapter.markForDeletion()
    await adapter.unmarkForDeletion()
    await adapter.post()
    await adapter.unpost()
    expect(args.doc.value).toEqual({ state: 'draft' })
    expect(args.toasts).toHaveLength(2)
  })
})

describe('property-management catalog editor persistence', () => {
  beforeEach(() => vi.clearAllMocks())

  it('initializes a new catalog and clears document and lease state', async () => {
    const args = context({ isNew: ref(true), initialFields: ref({ name: 'Initial' }) })
    await useCatalogEntityEditorPersistence(args as never).load()
    expect(args.doc.value).toBeNull()
    expect(args.leaseEditor.applyInitialParts).toHaveBeenCalledWith(null)
    expect(mocks.sanitizeNgbEditorModelForEditing).toHaveBeenCalledOnce()
  })

  it('loads an existing catalog and clears persisted lease rows', async () => {
    mocks.getCatalogById.mockResolvedValueOnce({ id: 'catalog-1', payload: { fields: { name: 'Loaded' } } })
    const args = context()
    await useCatalogEntityEditorPersistence(args as never).load()
    expect(args.model.value).toEqual({ name: 'Loaded' })
    expect(args.leaseEditor.applyPersistedParts).toHaveBeenCalledWith(null)
  })

  it.each([
    { navigateOnCreate: true, mode: 'drawer', shouldNavigate: true },
    { navigateOnCreate: false, mode: 'page', shouldNavigate: false },
    { navigateOnCreate: null, mode: 'page', shouldNavigate: true },
    { navigateOnCreate: null, mode: 'drawer', shouldNavigate: false },
  ])('creates a catalog and resolves navigation %#', async ({ navigateOnCreate, mode, shouldNavigate }) => {
    mocks.createCatalog.mockResolvedValueOnce({ id: 'catalog-1', payload: { fields: {} } })
    const args = context({
      isNew: ref(true),
      navigateOnCreate: ref(navigateOnCreate),
      mode: ref(mode),
    })
    await useCatalogEntityEditorPersistence(args as never).save()
    expect(args.router.replace).toHaveBeenCalledTimes(shouldNavigate ? 1 : 0)
  })

  it('updates and fully rehydrates an existing catalog', async () => {
    mocks.updateCatalog.mockResolvedValueOnce({ id: 'catalog-1', payload: { fields: { name: 'Updated' } } })
    const args = context()
    await useCatalogEntityEditorPersistence(args as never).save()
    expect(args.model.value).toEqual({ name: 'Updated' })
    expect(args.emitSaved).toHaveBeenCalledOnce()
  })

  it('executes every catalog lifecycle mutation', async () => {
    const args = context()
    const adapter = useCatalogEntityEditorPersistence(args as never)
    await adapter.markForDeletion()
    await adapter.unmarkForDeletion()
    await adapter.deleteEntity()
    expect(mocks.markCatalogForDeletion).toHaveBeenCalledWith('pm.receivable_payment', 'doc-1')
    expect(mocks.unmarkCatalogForDeletion).toHaveBeenCalledWith('pm.receivable_payment', 'doc-1')
    expect(mocks.deleteCatalog).toHaveBeenCalledWith('pm.receivable_payment', 'doc-1')
    expect(args.toasts).toHaveLength(2)
  })
})
