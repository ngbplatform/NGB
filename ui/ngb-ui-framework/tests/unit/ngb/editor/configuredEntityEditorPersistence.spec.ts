import { computed, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  buildFieldsPayload: vi.fn(() => ({ persisted: true })),
  createCatalog: vi.fn(),
  createDraft: vi.fn(),
  deleteCatalog: vi.fn(),
  ensureModelKeys: vi.fn(),
  getCatalogById: vi.fn(),
  getDocumentEditorState: vi.fn(),
  getDocumentEffects: vi.fn(),
  hydrateEntityReferenceFieldsForEditing: vi.fn(),
  markCatalogForDeletion: vi.fn(),
  sanitizeNgbEditorModelForEditing: vi.fn(),
  syncNgbEditorComputedDisplay: vi.fn(),
  unmarkCatalogForDeletion: vi.fn(),
  updateCatalog: vi.fn(),
  updateDraft: vi.fn(),
}))

vi.mock('../../../../src/ngb/api/catalogs', () => ({
  createCatalog: mocks.createCatalog,
  deleteCatalog: mocks.deleteCatalog,
  getCatalogById: mocks.getCatalogById,
  markCatalogForDeletion: mocks.markCatalogForDeletion,
  unmarkCatalogForDeletion: mocks.unmarkCatalogForDeletion,
  updateCatalog: mocks.updateCatalog,
}))

vi.mock('../../../../src/ngb/api/documents', () => ({
  createDraft: mocks.createDraft,
  getDocumentEditorState: mocks.getDocumentEditorState,
  getDocumentEffects: mocks.getDocumentEffects,
  updateDraft: mocks.updateDraft,
}))

vi.mock('../../../../src/ngb/metadata/entityForm', () => ({
  buildFieldsPayload: mocks.buildFieldsPayload,
  ensureModelKeys: mocks.ensureModelKeys,
}))

vi.mock('../../../../src/ngb/metadata/referenceHydration', () => ({
  hydrateEntityReferenceFieldsForEditing: mocks.hydrateEntityReferenceFieldsForEditing,
}))

vi.mock('../../../../src/ngb/editor/config', () => ({
  sanitizeNgbEditorModelForEditing: mocks.sanitizeNgbEditorModelForEditing,
  syncNgbEditorComputedDisplay: mocks.syncNgbEditorComputedDisplay,
}))

import {
  createConfiguredCatalogEntityEditorPersistence,
  createConfiguredDocumentEntityEditorPersistence,
} from '../../../../src/ngb/editor/entityEditorPersistence'

function context(overrides: Record<string, unknown> = {}) {
  const typeCode = ref('crm.lead')
  const isNew = ref(false)
  return {
    kind: computed(() => 'document' as const),
    typeCode: computed(() => typeCode.value),
    currentId: ref<string | null>('entity-1'),
    isNew: computed(() => isNew.value),
    metadata: computed(() => ({ form: { sections: [] } })),
    catalogMeta: ref<unknown>(null),
    docMeta: ref<unknown>(null),
    catalogItem: ref<unknown>(null),
    doc: ref<unknown>(null),
    docEffects: ref<unknown>(null),
    model: ref<Record<string, unknown>>({ stale: true }),
    partsModel: ref<unknown>(null),
    lookupStore: { marker: 'lookup' },
    initialFields: computed(() => null),
    initialParts: computed(() => null),
    ensureCatalogMetadata: vi.fn().mockResolvedValue({ form: { sections: [] } }),
    ensureDocumentMetadata: vi.fn().mockResolvedValue({ form: { sections: [] }, parts: [{ key: 'lines' }] }),
    currentEditorContext: vi.fn(() => ({ kind: 'document', typeCode: typeCode.value })),
    resetInitialSnapshot: vi.fn(),
    setEditorError: vi.fn(),
    onCreated: vi.fn(),
    onSaved: vi.fn(),
    __typeCode: typeCode,
    __isNew: isNew,
    ...overrides,
  }
}

function strategy() {
  return {
    buildPayload: vi.fn(() => ({ lines: [{ amount: 12 }] })),
    hydrate: vi.fn().mockResolvedValue(undefined),
    synchronize: vi.fn(),
  }
}

describe('configured catalog entity persistence', () => {
  beforeEach(() => vi.clearAllMocks())

  it('initializes a new catalog and applies cloned initial values', async () => {
    const initial = { name: 'Acme', nested: { value: 1 }, skip: undefined }
    const args = context({
      initialFields: computed(() => initial),
    })
    args.__isNew.value = true

    await createConfiguredCatalogEntityEditorPersistence(args as never).load()

    expect(args.doc.value).toBeNull()
    expect(args.docEffects.value).toBeNull()
    expect(args.partsModel.value).toBeNull()
    expect(args.model.value).toEqual({ name: 'Acme', nested: { value: 1 } })
    expect(args.model.value.nested).not.toBe(initial.nested)
    expect(mocks.hydrateEntityReferenceFieldsForEditing).toHaveBeenCalledOnce()
    expect(mocks.sanitizeNgbEditorModelForEditing).toHaveBeenCalledOnce()
    expect(mocks.syncNgbEditorComputedDisplay).toHaveBeenCalledOnce()
    expect(args.resetInitialSnapshot).toHaveBeenCalledOnce()
  })

  it('loads an existing catalog and normalizes missing fields', async () => {
    const item = { id: 'entity-1', payload: null }
    mocks.getCatalogById.mockResolvedValueOnce(item)
    const args = context()

    await createConfiguredCatalogEntityEditorPersistence(args as never).load()

    expect(mocks.getCatalogById).toHaveBeenCalledWith('crm.lead', 'entity-1')
    expect(args.catalogItem.value).toEqual(item)
    expect(args.model.value).toEqual({})
  })

  it('normalizes an undefined initial-field payload for a new catalog', async () => {
    const args = context({ initialFields: computed(() => undefined) })
    args.__isNew.value = true
    await createConfiguredCatalogEntityEditorPersistence(args as never).load()
    expect(args.model.value).toEqual({})
  })

  it('creates and updates catalogs with the same hydration lifecycle', async () => {
    mocks.createCatalog.mockResolvedValueOnce({ id: 'created-1', payload: { fields: {} } })
    const createArgs = context()
    createArgs.__isNew.value = true
    const createAdapter = createConfiguredCatalogEntityEditorPersistence(createArgs as never)

    await createAdapter.save()

    expect(mocks.createCatalog).toHaveBeenCalledWith('crm.lead', { fields: { persisted: true } })
    expect(createArgs.currentId.value).toBe('created-1')
    expect(createArgs.onCreated).toHaveBeenCalledWith('created-1')

    const updated = { id: 'entity-1', payload: { fields: { name: 'Updated' } } }
    mocks.updateCatalog.mockResolvedValueOnce(updated)
    const updateArgs = context()

    await createConfiguredCatalogEntityEditorPersistence(updateArgs as never).save()

    expect(updateArgs.model.value).toEqual({ name: 'Updated' })
    expect(updateArgs.onSaved).toHaveBeenCalledOnce()
    expect(mocks.hydrateEntityReferenceFieldsForEditing).toHaveBeenCalled()
  })

  it('delegates every catalog lifecycle mutation', async () => {
    const args = context()
    const adapter = createConfiguredCatalogEntityEditorPersistence(args as never)

    await adapter.markForDeletion()
    await adapter.unmarkForDeletion()
    await adapter.deleteEntity()

    expect(mocks.markCatalogForDeletion).toHaveBeenCalledWith('crm.lead', 'entity-1')
    expect(mocks.unmarkCatalogForDeletion).toHaveBeenCalledWith('crm.lead', 'entity-1')
    expect(mocks.deleteCatalog).toHaveBeenCalledWith('crm.lead', 'entity-1')
  })
})

describe('configured document entity persistence', () => {
  beforeEach(() => vi.clearAllMocks())

  it('loads effects and degrades to an empty snapshot when effects are unavailable', async () => {
    const args = context()
    const adapter = createConfiguredDocumentEntityEditorPersistence(args as never, strategy())
    mocks.getDocumentEffects.mockResolvedValueOnce({ entries: [{ id: 1 }] })

    await adapter.loadEffectsSnapshot?.('crm.lead', 'entity-1')
    expect(args.docEffects.value).toEqual({ entries: [{ id: 1 }] })

    mocks.getDocumentEffects.mockRejectedValueOnce(new Error('offline'))
    await adapter.loadEffectsSnapshot?.('crm.lead', 'entity-1')
    expect(args.docEffects.value).toBeNull()
  })

  it('initializes a new document and clones initial parts', async () => {
    const initialParts = { lines: [{ amount: 5 }] }
    const args = context({
      initialFields: computed(() => ({ memo: 'Initial' })),
      initialParts: computed(() => initialParts),
    })
    args.__isNew.value = true
    const policy = strategy()

    await createConfiguredDocumentEntityEditorPersistence(args as never, policy).load()

    expect(args.catalogItem.value).toBeNull()
    expect(args.doc.value).toBeNull()
    expect(args.docEffects.value).toBeNull()
    expect(args.model.value).toEqual({ memo: 'Initial' })
    expect(args.partsModel.value).toEqual(initialParts)
    expect(args.partsModel.value).not.toBe(initialParts)
    expect(policy.hydrate).toHaveBeenCalledOnce()
    expect(policy.synchronize).toHaveBeenCalledOnce()
  })

  it('uses null defaults for a new document without initial values', async () => {
    const args = context()
    args.__isNew.value = true

    await createConfiguredDocumentEntityEditorPersistence(args as never, strategy()).load()

    expect(args.partsModel.value).toBeNull()
    expect(args.model.value).toEqual({})
  })

  it('loads existing documents with and without parts', async () => {
    mocks.getDocumentEditorState
      .mockResolvedValueOnce({
        document: { id: 'entity-1', payload: { fields: { memo: 'Loaded' }, parts: { lines: [] } } },
      })
      .mockResolvedValueOnce({ document: { id: 'entity-2', payload: null } })
    const first = context()
    const firstPolicy = strategy()

    await createConfiguredDocumentEntityEditorPersistence(first as never, firstPolicy).load()
    expect(first.model.value).toEqual({ memo: 'Loaded' })
    expect(first.partsModel.value).toEqual({ lines: [] })

    const second = context({ currentId: ref('entity-2') })
    await createConfiguredDocumentEntityEditorPersistence(second as never, strategy()).load()
    expect(second.model.value).toEqual({})
    expect(second.partsModel.value).toBeNull()
  })

  it('creates drafts using server parts and outgoing-payload fallback', async () => {
    mocks.createDraft
      .mockResolvedValueOnce({ id: 'created-1', payload: { parts: { lines: ['server'] } } })
      .mockResolvedValueOnce({ id: 'created-2', payload: {} })
    const first = context()
    first.__isNew.value = true
    const firstPolicy = strategy()

    await createConfiguredDocumentEntityEditorPersistence(first as never, firstPolicy).save()

    expect(mocks.createDraft).toHaveBeenCalledWith('crm.lead', {
      fields: { persisted: true },
      parts: { lines: [{ amount: 12 }] },
    })
    expect(first.partsModel.value).toEqual({ lines: ['server'] })
    expect(first.onCreated).toHaveBeenCalledWith('created-1')

    const second = context()
    second.__isNew.value = true
    await createConfiguredDocumentEntityEditorPersistence(second as never, strategy()).save()
    expect(second.partsModel.value).toEqual({ lines: [{ amount: 12 }] })
  })

  it('updates drafts and fully rehydrates server state', async () => {
    mocks.updateDraft
      .mockResolvedValueOnce({ id: 'entity-1', payload: { fields: { memo: 'Updated' }, parts: { lines: ['server'] } } })
      .mockResolvedValueOnce({ id: 'entity-1', payload: { fields: null } })
    const first = context()
    const firstPolicy = strategy()

    await createConfiguredDocumentEntityEditorPersistence(first as never, firstPolicy).save()

    expect(first.model.value).toEqual({ memo: 'Updated' })
    expect(first.partsModel.value).toEqual({ lines: ['server'] })
    expect(first.onSaved).toHaveBeenCalledOnce()
    expect(firstPolicy.synchronize).toHaveBeenCalledTimes(2)

    const second = context({ docMeta: ref(null) })
    const secondPolicy = strategy()
    await createConfiguredDocumentEntityEditorPersistence(second as never, secondPolicy).save()
    expect(second.model.value).toEqual({})
    expect(second.partsModel.value).toEqual({ lines: [{ amount: 12 }] })
    expect(secondPolicy.buildPayload).toHaveBeenCalledWith(expect.objectContaining({ partsMeta: undefined }))
  })
})
