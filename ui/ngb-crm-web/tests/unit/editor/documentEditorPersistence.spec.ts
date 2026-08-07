import { ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  applyInitialFieldValues: vi.fn(),
  buildCatalogFullPageUrl: vi.fn(() => '/catalog/full-page'),
  buildAgencyBillingDocumentPartsPayload: vi.fn(() => ({ lines: ['payload'] })),
  buildCRMDocumentPartsPayload: vi.fn(() => ({ lines: ['payload'] })),
  buildTradeDocumentPartsPayload: vi.fn(() => ({ lines: ['payload'] })),
  buildDocumentFullPageUrl: vi.fn(() => '/document/full-page'),
  buildFieldsPayload: vi.fn(() => ({ memo: 'payload' })),
  createCatalog: vi.fn(),
  createDraft: vi.fn(),
  deleteCatalog: vi.fn(),
  ensureModelKeys: vi.fn(),
  getCatalogById: vi.fn(),
  getDocumentEditorState: vi.fn(),
  getDocumentEffects: vi.fn(),
  hydrateCRMDocumentPartLookupRows: vi.fn(),
  hydrateAgencyBillingDocumentPartLookupRows: vi.fn(),
  hydrateTradeDocumentPartLookupRows: vi.fn(),
  hydrateEntityReferenceFieldsForEditing: vi.fn(),
  markCatalogForDeletion: vi.fn(),
  markDocumentForDeletion: vi.fn(),
  postDocument: vi.fn(),
  resolveNavigateOnCreate: vi.fn(() => false),
  sanitizeNgbEditorModelForEditing: vi.fn(),
  syncCRMDocumentAmountField: vi.fn(),
  syncAgencyBillingDocumentComputedFields: vi.fn(),
  syncTradeDocumentAmountField: vi.fn(),
  syncNgbEditorComputedDisplay: vi.fn(),
  unmarkCatalogForDeletion: vi.fn(),
  unmarkDocumentForDeletion: vi.fn(),
  unpostDocument: vi.fn(),
  updateCatalog: vi.fn(),
  updateDraft: vi.fn(),
}))

vi.mock('@ngbplatform/ui', () => ({
  ...mocks,
  clonePlainData: (value: unknown) => value == null ? value : JSON.parse(JSON.stringify(value)),
  setModelFromFields: (model: { value: unknown }, fields: unknown) => { model.value = fields },
}))

vi.mock('../../../src/editor/documentParts', () => ({
  buildCRMDocumentPartsPayload: mocks.buildCRMDocumentPartsPayload,
  hydrateCRMDocumentPartLookupRows: mocks.hydrateCRMDocumentPartLookupRows,
  syncCRMDocumentAmountField: mocks.syncCRMDocumentAmountField,
}))

vi.mock('../../../../ngb-agency-billing-web/src/editor/documentParts', () => ({
  buildAgencyBillingDocumentPartsPayload: mocks.buildAgencyBillingDocumentPartsPayload,
  hydrateAgencyBillingDocumentPartLookupRows: mocks.hydrateAgencyBillingDocumentPartLookupRows,
  syncAgencyBillingDocumentComputedFields: mocks.syncAgencyBillingDocumentComputedFields,
}))

vi.mock('../../../../ngb-trade-web/src/editor/documentParts', () => ({
  buildTradeDocumentPartsPayload: mocks.buildTradeDocumentPartsPayload,
  hydrateTradeDocumentPartLookupRows: mocks.hydrateTradeDocumentPartLookupRows,
  syncTradeDocumentAmountField: mocks.syncTradeDocumentAmountField,
}))

import { useCatalogEntityEditorPersistence } from '../../../src/editor/useCatalogEntityEditorPersistence'
import { useDocumentEntityEditorPersistence } from '../../../src/editor/useDocumentEntityEditorPersistence'
import { useCatalogEntityEditorPersistence as useAgencyCatalogPersistence } from '../../../../ngb-agency-billing-web/src/editor/useCatalogEntityEditorPersistence'
import { useDocumentEntityEditorPersistence as useAgencyDocumentPersistence } from '../../../../ngb-agency-billing-web/src/editor/useDocumentEntityEditorPersistence'
import { useCatalogEntityEditorPersistence as useTradeCatalogPersistence } from '../../../../ngb-trade-web/src/editor/useCatalogEntityEditorPersistence'
import { useDocumentEntityEditorPersistence as useTradeDocumentPersistence } from '../../../../ngb-trade-web/src/editor/useDocumentEntityEditorPersistence'

function context(overrides: Record<string, unknown> = {}) {
  return {
    typeCode: ref('crm.lead_intake'),
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
    partsModel: ref<unknown>(null),
    initialParts: ref<unknown>(null),
    initialFields: ref<unknown>(null),
    metaStore: {
      ensureDocumentType: vi.fn().mockResolvedValue({ form: {}, parts: [] }),
      ensureCatalogType: vi.fn().mockResolvedValue({ form: {} }),
    },
    lookupStore: {},
    currentEditorContext: vi.fn(() => ({ context: true })),
    resetInitialSnapshot: vi.fn(),
    emitCreated: vi.fn(),
    emitSaved: vi.fn(),
    router: { replace: vi.fn() },
    toasts: [] as unknown[],
    ...overrides,
  }
}

describe('crm document editor persistence', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.buildCRMDocumentPartsPayload.mockReturnValue({ lines: ['payload'] })
    mocks.buildFieldsPayload.mockReturnValue({ memo: 'payload' })
    mocks.resolveNavigateOnCreate.mockReturnValue(false)
  })

  it('loads effects and recovers when effects are unavailable', async () => {
    const args = context()
    const adapter = useDocumentEntityEditorPersistence(args as never)
    mocks.getDocumentEffects.mockResolvedValueOnce({ entries: [1] })
    await adapter.loadEffectsSnapshot('crm.lead_intake', 'doc-1')
    expect(args.docEffects.value).toEqual({ entries: [1] })

    mocks.getDocumentEffects.mockRejectedValueOnce(new Error('offline'))
    await adapter.loadEffectsSnapshot('crm.lead_intake', 'doc-1')
    expect(args.docEffects.value).toBeNull()
  })

  it('initializes and hydrates a new document', async () => {
    const args = context({
      isNew: ref(true),
      initialFields: ref({ subject: 'Initial' }),
      initialParts: ref({ lines: [{ amount: 2 }] }),
    })

    await useDocumentEntityEditorPersistence(args as never).load()

    expect(args.doc.value).toBeNull()
    expect(args.docEffects.value).toBeNull()
    expect(args.partsModel.value).toEqual({ lines: [{ amount: 2 }] })
    expect(mocks.applyInitialFieldValues).toHaveBeenCalledWith(args.model.value, { subject: 'Initial' })
    expect(mocks.hydrateCRMDocumentPartLookupRows).toHaveBeenCalledOnce()
    expect(mocks.syncCRMDocumentAmountField).toHaveBeenCalledOnce()
    expect(mocks.syncNgbEditorComputedDisplay).toHaveBeenCalledOnce()
    expect(args.resetInitialSnapshot).toHaveBeenCalledOnce()
  })

  it('uses null defaults while initializing a new document', async () => {
    const args = context({ isNew: ref(true) })
    await useDocumentEntityEditorPersistence(args as never).load()
    expect(args.partsModel.value).toBeNull()
    expect(mocks.applyInitialFieldValues).toHaveBeenCalledWith(args.model.value, null)
  })

  it('hydrates an existing editor from the unified editor-state endpoint', async () => {
    const document = {
      id: 'doc-1',
      status: 2,
      isMarkedForDeletion: false,
      payload: { fields: { memo: 'loaded' }, parts: { lines: [] } },
    }
    mocks.getDocumentEditorState.mockResolvedValueOnce({ document, documentVersion: 2, actions: [] })
    const args = context()

    await useDocumentEntityEditorPersistence(args as never).load()

    expect(mocks.getDocumentEditorState).toHaveBeenCalledWith('crm.lead_intake', 'doc-1')
    expect(args.doc.value).toEqual(document)
    expect(args.model.value).toEqual({ memo: 'loaded' })
    expect(args.partsModel.value).toEqual({ lines: [] })
    expect(mocks.getDocumentEffects).not.toHaveBeenCalled()
    expect(args.resetInitialSnapshot).toHaveBeenCalledOnce()
  })

  it('hydrates an existing document with no parts as null', async () => {
    mocks.getDocumentEditorState.mockResolvedValueOnce({
      document: { id: 'doc-1', payload: { fields: {} } },
      documentVersion: 1,
      actions: [],
    })
    const args = context()
    await useDocumentEntityEditorPersistence(args as never).load()
    expect(args.partsModel.value).toBeNull()
  })

  it('creates a draft, hydrates response parts, emits, and navigates when requested', async () => {
    const created = { id: 'created-1', payload: { parts: { lines: ['server'] } } }
    mocks.createDraft.mockResolvedValueOnce(created)
    mocks.resolveNavigateOnCreate.mockReturnValueOnce(true)
    const args = context({ isNew: ref(true) })

    await useDocumentEntityEditorPersistence(args as never).save()

    expect(mocks.createDraft).toHaveBeenCalledWith('crm.lead_intake', {
      fields: { memo: 'payload' },
      parts: { lines: ['payload'] },
    })
    expect(args.currentId.value).toBe('created-1')
    expect(args.partsModel.value).toEqual({ lines: ['server'] })
    expect(args.emitCreated).toHaveBeenCalledWith('created-1')
    expect(args.router.replace).toHaveBeenCalledWith('/document/full-page')
  })

  it('falls back to outgoing parts and stays in place after draft creation', async () => {
    mocks.createDraft.mockResolvedValueOnce({ id: 'created-2', payload: {} })
    const args = context({ isNew: ref(true) })
    await useDocumentEntityEditorPersistence(args as never).save()
    expect(args.partsModel.value).toEqual({ lines: ['payload'] })
    expect(args.router.replace).not.toHaveBeenCalled()
  })

  it('updates an existing draft and fully rehydrates its model', async () => {
    const updated = { id: 'doc-1', payload: { fields: { memo: 'updated' } } }
    mocks.updateDraft.mockResolvedValueOnce(updated)
    const args = context()

    await useDocumentEntityEditorPersistence(args as never).save()

    expect(mocks.updateDraft).toHaveBeenCalledWith('crm.lead_intake', 'doc-1', {
      fields: { memo: 'payload' },
      parts: { lines: ['payload'] },
    })
    expect(args.model.value).toEqual({ memo: 'updated' })
    expect(args.partsModel.value).toEqual({ lines: ['payload'] })
    expect(mocks.hydrateEntityReferenceFieldsForEditing).toHaveBeenCalledOnce()
    expect(args.emitSaved).toHaveBeenCalledOnce()
  })

  it('executes every document lifecycle mutation', async () => {
    const marked = { id: 'doc-1', state: 'marked' }
    const restored = { id: 'doc-1', state: 'restored' }
    const posted = { id: 'doc-1', state: 'posted' }
    const draft = { id: 'doc-1', state: 'draft' }
    mocks.markDocumentForDeletion.mockResolvedValueOnce(marked)
    mocks.unmarkDocumentForDeletion.mockResolvedValueOnce(restored)
    mocks.postDocument.mockResolvedValueOnce(posted)
    mocks.unpostDocument.mockResolvedValueOnce(draft)
    const args = context()
    const adapter = useDocumentEntityEditorPersistence(args as never)

    await adapter.markForDeletion()
    await adapter.unmarkForDeletion()
    await adapter.post()
    await adapter.unpost()

    expect(args.doc.value).toEqual(draft)
    expect(args.toasts).toEqual([
      { title: 'Deleted', message: 'Document marked for deletion.', tone: 'warn' },
      { title: 'Restored', message: 'Document restored.', tone: 'success' },
    ])
  })
})

describe('crm catalog editor persistence', () => {
  beforeEach(() => vi.clearAllMocks())

  it('initializes a new catalog entity and clears document state', async () => {
    const args = context({ isNew: ref(true), initialFields: ref({ name: 'Initial' }) })
    await useCatalogEntityEditorPersistence(args as never).load()
    expect(args.doc.value).toBeNull()
    expect(args.partsModel.value).toBeNull()
    expect(mocks.applyInitialFieldValues).toHaveBeenCalledWith(args.model.value, { name: 'Initial' })
    expect(mocks.sanitizeNgbEditorModelForEditing).toHaveBeenCalledOnce()
    expect(args.resetInitialSnapshot).toHaveBeenCalledOnce()
  })

  it('initializes with null fields when none are supplied', async () => {
    const args = context({ isNew: ref(true) })
    await useCatalogEntityEditorPersistence(args as never).load()
    expect(mocks.applyInitialFieldValues).toHaveBeenCalledWith(args.model.value, null)
  })

  it('loads and hydrates an existing catalog entity', async () => {
    mocks.getCatalogById.mockResolvedValueOnce({ id: 'doc-1', payload: { fields: { name: 'Loaded' } } })
    const args = context()
    await useCatalogEntityEditorPersistence(args as never).load()
    expect(args.catalogItem.value).toEqual({ id: 'doc-1', payload: { fields: { name: 'Loaded' } } })
    expect(args.model.value).toEqual({ name: 'Loaded' })
    expect(mocks.hydrateEntityReferenceFieldsForEditing).toHaveBeenCalledOnce()
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
    expect(args.emitCreated).toHaveBeenCalledWith('catalog-1')
    expect(args.router.replace).toHaveBeenCalledTimes(shouldNavigate ? 1 : 0)
  })

  it('updates and rehydrates an existing catalog entity', async () => {
    mocks.updateCatalog.mockResolvedValueOnce({ id: 'doc-1', payload: { fields: { name: 'Updated' } } })
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
    expect(mocks.markCatalogForDeletion).toHaveBeenCalledWith('crm.lead_intake', 'doc-1')
    expect(mocks.unmarkCatalogForDeletion).toHaveBeenCalledWith('crm.lead_intake', 'doc-1')
    expect(mocks.deleteCatalog).toHaveBeenCalledWith('crm.lead_intake', 'doc-1')
    expect(args.toasts).toHaveLength(2)
  })
})

describe.each([
  {
    name: 'agency billing',
    typeCode: 'ab.invoice',
    documentAdapter: useAgencyDocumentPersistence,
    catalogAdapter: useAgencyCatalogPersistence,
    buildParts: mocks.buildAgencyBillingDocumentPartsPayload,
  },
  {
    name: 'trade',
    typeCode: 'trd.sales_invoice',
    documentAdapter: useTradeDocumentPersistence,
    catalogAdapter: useTradeCatalogPersistence,
    buildParts: mocks.buildTradeDocumentPartsPayload,
  },
])('$name persistence full contract', ({ typeCode, documentAdapter, catalogAdapter, buildParts }) => {
  beforeEach(() => {
    vi.clearAllMocks()
    buildParts.mockReturnValue({ lines: ['payload'] })
    mocks.buildFieldsPayload.mockReturnValue({ memo: 'payload' })
    mocks.resolveNavigateOnCreate.mockReturnValue(false)
  })

  it('covers new, existing, create, update, effects, and lifecycle document paths', async () => {
    const effectsArgs = context({ typeCode: ref(typeCode) })
    const effectsAdapter = documentAdapter(effectsArgs as never)
    mocks.getDocumentEffects.mockResolvedValueOnce({ entries: [1] })
    await effectsAdapter.loadEffectsSnapshot(typeCode, 'doc-1')
    expect(effectsArgs.docEffects.value).toEqual({ entries: [1] })
    mocks.getDocumentEffects.mockRejectedValueOnce(new Error('offline'))
    await effectsAdapter.loadEffectsSnapshot(typeCode, 'doc-1')
    expect(effectsArgs.docEffects.value).toBeNull()

    const newArgs = context({
      typeCode: ref(typeCode),
      isNew: ref(true),
      initialFields: ref({ memo: 'initial' }),
      initialParts: ref({ lines: ['initial'] }),
    })
    await documentAdapter(newArgs as never).load()
    expect(newArgs.partsModel.value).toEqual({ lines: ['initial'] })
    const emptyNewArgs = context({ typeCode: ref(typeCode), isNew: ref(true) })
    await documentAdapter(emptyNewArgs as never).load()
    expect(emptyNewArgs.partsModel.value).toBeNull()

    mocks.getDocumentEditorState
      .mockResolvedValueOnce({ document: { id: 'doc-1', payload: { fields: { memo: 'loaded' }, parts: { lines: ['server'] } } } })
      .mockResolvedValueOnce({ document: { id: 'doc-1', payload: { fields: {} } } })
    const existingArgs = context({ typeCode: ref(typeCode) })
    await documentAdapter(existingArgs as never).load()
    expect(existingArgs.partsModel.value).toEqual({ lines: ['server'] })
    const noPartsArgs = context({ typeCode: ref(typeCode) })
    await documentAdapter(noPartsArgs as never).load()
    expect(noPartsArgs.partsModel.value).toBeNull()

    mocks.resolveNavigateOnCreate.mockReturnValueOnce(true).mockReturnValueOnce(false)
    mocks.createDraft
      .mockResolvedValueOnce({ id: 'created-1', payload: { parts: { lines: ['created'] } } })
      .mockResolvedValueOnce({ id: 'created-2', payload: {} })
    const createNavigateArgs = context({ typeCode: ref(typeCode), isNew: ref(true) })
    await documentAdapter(createNavigateArgs as never).save()
    expect(createNavigateArgs.router.replace).toHaveBeenCalledWith('/document/full-page')
    const createStayArgs = context({ typeCode: ref(typeCode), isNew: ref(true) })
    await documentAdapter(createStayArgs as never).save()
    expect(createStayArgs.partsModel.value).toEqual({ lines: ['payload'] })
    expect(createStayArgs.router.replace).not.toHaveBeenCalled()

    mocks.updateDraft.mockResolvedValueOnce({ id: 'doc-1', payload: { fields: { memo: 'updated' } } })
    const updateArgs = context({ typeCode: ref(typeCode) })
    await documentAdapter(updateArgs as never).save()
    expect(updateArgs.model.value).toEqual({ memo: 'updated' })
    expect(updateArgs.partsModel.value).toEqual({ lines: ['payload'] })

    mocks.markDocumentForDeletion.mockResolvedValueOnce({ state: 'marked' })
    mocks.unmarkDocumentForDeletion.mockResolvedValueOnce({ state: 'restored' })
    mocks.postDocument.mockResolvedValueOnce({ state: 'posted' })
    mocks.unpostDocument.mockResolvedValueOnce({ state: 'draft' })
    const lifecycleArgs = context({ typeCode: ref(typeCode) })
    const lifecycle = documentAdapter(lifecycleArgs as never)
    await lifecycle.markForDeletion()
    await lifecycle.unmarkForDeletion()
    await lifecycle.post()
    await lifecycle.unpost()
    expect(lifecycleArgs.toasts).toHaveLength(2)
    expect(lifecycleArgs.doc.value).toEqual({ state: 'draft' })
  })

  it('covers new, existing, create, update, and lifecycle catalog paths', async () => {
    const newArgs = context({ typeCode: ref(typeCode), isNew: ref(true), initialFields: ref({ name: 'Initial' }) })
    await catalogAdapter(newArgs as never).load()
    const emptyNewArgs = context({ typeCode: ref(typeCode), isNew: ref(true) })
    await catalogAdapter(emptyNewArgs as never).load()

    mocks.getCatalogById.mockResolvedValueOnce({ id: 'catalog-1', payload: { fields: { name: 'Loaded' } } })
    const existingArgs = context({ typeCode: ref(typeCode) })
    await catalogAdapter(existingArgs as never).load()
    expect(existingArgs.model.value).toEqual({ name: 'Loaded' })

    mocks.createCatalog.mockResolvedValue({ id: 'created-1', payload: { fields: {} } })
    for (const [navigateOnCreate, mode, shouldNavigate] of [
      [true, 'drawer', true],
      [false, 'page', false],
      [null, 'page', true],
      [null, 'drawer', false],
    ] as const) {
      const createArgs = context({
        typeCode: ref(typeCode),
        isNew: ref(true),
        navigateOnCreate: ref(navigateOnCreate),
        mode: ref(mode),
      })
      await catalogAdapter(createArgs as never).save()
      expect(createArgs.router.replace).toHaveBeenCalledTimes(shouldNavigate ? 1 : 0)
    }

    mocks.updateCatalog.mockResolvedValueOnce({ id: 'catalog-1', payload: { fields: { name: 'Updated' } } })
    const updateArgs = context({ typeCode: ref(typeCode) })
    await catalogAdapter(updateArgs as never).save()
    expect(updateArgs.model.value).toEqual({ name: 'Updated' })

    const lifecycleArgs = context({ typeCode: ref(typeCode) })
    const lifecycle = catalogAdapter(lifecycleArgs as never)
    await lifecycle.markForDeletion()
    await lifecycle.unmarkForDeletion()
    await lifecycle.deleteEntity()
    expect(lifecycleArgs.toasts).toHaveLength(2)
  })
})
