import { computed, ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'

vi.mock('../../../../src/ngb/api/catalogs', () => ({
  createCatalog: vi.fn(),
  deleteCatalog: vi.fn(),
  getCatalogById: vi.fn(),
  markCatalogForDeletion: vi.fn(),
  unmarkCatalogForDeletion: vi.fn(),
  updateCatalog: vi.fn(),
}))

vi.mock('../../../../src/ngb/api/documents', () => ({
  createDraft: vi.fn(),
  getDocumentEditorState: vi.fn(),
  getDocumentEffects: vi.fn(),
  updateDraft: vi.fn(),
}))

import {
  applyInitialFieldValues,
  setModelFromFields,
  useEntityEditorPersistence,
} from '../../../../src/ngb/editor/entityEditorPersistence'

function createPersistenceHarness() {
  const kind = ref<'catalog' | 'document'>('document')
  const typeCode = ref('pm.invoice')
  const metadata = ref<{ form?: unknown | null } | null>({ form: { sections: [] } })
  const loading = ref(false)
  const saving = ref(false)
  const canSave = ref(true)
  const canMarkForDeletion = ref(true)
  const canUnmarkForDeletion = ref(true)
  const canDelete = ref(true)
  const isNew = ref(false)
  const error = ref<{ summary: string } | null>(null)
  const emitChanged = vi.fn()
  const emitDeleted = vi.fn()
  const onMarkedForDeletion = vi.fn()
  const onUnmarkedForDeletion = vi.fn()

  const catalog = {
    load: vi.fn().mockResolvedValue(undefined),
    save: vi.fn().mockResolvedValue(undefined),
    markForDeletion: vi.fn().mockResolvedValue(undefined),
    unmarkForDeletion: vi.fn().mockResolvedValue(undefined),
    deleteEntity: vi.fn().mockResolvedValue(undefined),
  }

  const document = {
    load: vi.fn().mockResolvedValue(undefined),
    save: vi.fn().mockResolvedValue(undefined),
    loadEffectsSnapshot: vi.fn().mockResolvedValue(undefined),
  }

  const normalizeEditorError = vi.fn((cause: unknown) => ({
    summary: cause instanceof Error ? cause.message : String(cause),
  }))

  function setEditorError(value: { summary: string } | null) {
    error.value = value
  }

  const persistence = useEntityEditorPersistence({
    kind: computed(() => kind.value),
    typeCode: computed(() => typeCode.value),
    metadata: computed(() => metadata.value),
    loading,
    saving,
    canSave: computed(() => canSave.value),
    canMarkForDeletion: computed(() => canMarkForDeletion.value),
    canUnmarkForDeletion: computed(() => canUnmarkForDeletion.value),
    canDelete: computed(() => canDelete.value),
    isNew: computed(() => isNew.value),
    setEditorError,
    normalizeEditorError,
    emitChanged,
    emitDeleted,
    onMarkedForDeletion,
    onUnmarkedForDeletion,
    adapters: {
      catalog,
      document,
    },
  })

  return {
    state: {
      kind,
      typeCode,
      metadata,
      loading,
      saving,
      canSave,
      canMarkForDeletion,
      canUnmarkForDeletion,
      canDelete,
      isNew,
      error,
    },
    adapters: {
      catalog,
      document,
    },
    spies: {
      emitChanged,
      emitDeleted,
      normalizeEditorError,
      onMarkedForDeletion,
      onUnmarkedForDeletion,
    },
    persistence,
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (cause: unknown) => void
  const promise = new Promise<T>((nextResolve, nextReject) => {
    resolve = nextResolve
    reject = nextReject
  })
  return { promise, resolve, reject }
}

describe('entity editor persistence', () => {
  it('applies initial values by cloning nested data and replaces models from payload fields', () => {
    const target = {
      title: 'Old',
      details: {
        count: 1,
      },
    } as Record<string, unknown>

    const source = {
      title: 'Invoice INV-001',
      details: {
        count: 2,
      },
      tags: ['rent', 'utilities'],
      skip: undefined,
    } as Record<string, unknown>

    applyInitialFieldValues(target, source)

    expect(target).toEqual({
      title: 'Invoice INV-001',
      details: {
        count: 2,
      },
      tags: ['rent', 'utilities'],
    })
    expect(target.details).not.toBe(source.details)
    expect(target.tags).not.toBe(source.tags)

    const model = ref<Record<string, unknown>>({
      stale: true,
    })
    setModelFromFields(model, {
      customer_id: 'customer-1',
      amount: 1250,
    })
    expect(model.value).toEqual({
      customer_id: 'customer-1',
      amount: 1250,
    })

    setModelFromFields(model, null)
    expect(model.value).toEqual({})
  })

  it('loads and saves through the active adapter and captures normalized errors', async () => {
    const { state, adapters, spies, persistence } = createPersistenceHarness()

    state.kind.value = 'catalog'
    await persistence.load()
    await persistence.save()

    expect(adapters.catalog.load).toHaveBeenCalledTimes(1)
    expect(adapters.catalog.save).toHaveBeenCalledTimes(1)
    expect(adapters.document.load).not.toHaveBeenCalled()
    expect(adapters.document.save).not.toHaveBeenCalled()
    expect(state.loading.value).toBe(false)
    expect(state.saving.value).toBe(false)

    state.kind.value = 'document'
    adapters.document.load.mockRejectedValueOnce(new Error('load failed'))

    await persistence.load()

    expect(spies.normalizeEditorError).toHaveBeenCalledWith(expect.any(Error))
    expect(state.error.value).toEqual({
      summary: 'load failed',
    })

    adapters.document.save.mockRejectedValueOnce(new Error('save failed'))
    await persistence.save()
    expect(state.error.value).toEqual({ summary: 'save failed' })
    expect(state.saving.value).toBe(false)
  })

  it('ignores a failed load after a newer load has completed', async () => {
    const { state, adapters, persistence } = createPersistenceHarness()
    const stale = deferred<void>()
    adapters.document.load
      .mockImplementationOnce(() => stale.promise)
      .mockResolvedValueOnce(undefined)

    const staleLoad = persistence.load()
    await persistence.load()
    stale.reject(new Error('stale failure'))
    await staleLoad

    expect(state.error.value).toBeNull()
    expect(state.loading.value).toBe(false)
  })

  it('honors empty-type, missing-form, and capability guards', async () => {
    const { state, adapters, persistence } = createPersistenceHarness()

    state.typeCode.value = ''
    await persistence.load()
    expect(adapters.document.load).not.toHaveBeenCalled()

    state.metadata.value = null
    await persistence.save()
    state.metadata.value = { form: { sections: [] } }
    state.canSave.value = false
    await persistence.save()
    expect(adapters.document.save).not.toHaveBeenCalled()

    state.kind.value = 'catalog'
    state.isNew.value = true
    await persistence.markForDeletion()
    await persistence.unmarkForDeletion()
    await persistence.deleteEntity()
    state.isNew.value = false
    state.canMarkForDeletion.value = false
    state.canUnmarkForDeletion.value = false
    state.canDelete.value = false
    await persistence.markForDeletion()
    await persistence.unmarkForDeletion()
    await persistence.deleteEntity()
    expect(adapters.catalog.markForDeletion).not.toHaveBeenCalled()
    expect(adapters.catalog.unmarkForDeletion).not.toHaveBeenCalled()
    expect(adapters.catalog.deleteEntity).not.toHaveBeenCalled()
  })

  it('saves documents and ignores catalog-only mutations for document editors', async () => {
    const { state, adapters, persistence } = createPersistenceHarness()
    state.kind.value = 'document'

    await persistence.save()
    await persistence.markForDeletion()
    await persistence.unmarkForDeletion()
    await persistence.deleteEntity()

    expect(adapters.document.save).toHaveBeenCalledOnce()
    expect(adapters.catalog.markForDeletion).not.toHaveBeenCalled()
    expect(adapters.catalog.unmarkForDeletion).not.toHaveBeenCalled()
    expect(adapters.catalog.deleteEntity).not.toHaveBeenCalled()
  })

  it('marks, unmarks, and deletes catalog entities while reloading and emitting changes', async () => {
    const { state, adapters, spies, persistence } = createPersistenceHarness()

    state.kind.value = 'catalog'

    await persistence.markForDeletion()
    await persistence.unmarkForDeletion()
    await persistence.deleteEntity()

    expect(adapters.catalog.markForDeletion).toHaveBeenCalledTimes(1)
    expect(adapters.catalog.unmarkForDeletion).toHaveBeenCalledTimes(1)
    expect(adapters.catalog.deleteEntity).toHaveBeenCalledTimes(1)
    expect(adapters.catalog.load).toHaveBeenCalledTimes(3)
    expect(spies.emitChanged).toHaveBeenNthCalledWith(1, 'markForDeletion')
    expect(spies.emitChanged).toHaveBeenNthCalledWith(2, 'unmarkForDeletion')
    expect(spies.emitDeleted).toHaveBeenCalledTimes(1)
    expect(spies.onMarkedForDeletion).toHaveBeenCalledOnce()
    expect(spies.onUnmarkedForDeletion).toHaveBeenCalledOnce()
  })

  it('normalizes catalog lifecycle failures and always releases saving state', async () => {
    const { state, adapters, persistence } = createPersistenceHarness()
    state.kind.value = 'catalog'

    adapters.catalog.markForDeletion.mockRejectedValueOnce(new Error('mark failed'))
    await persistence.markForDeletion()
    expect(state.error.value).toEqual({ summary: 'mark failed' })
    expect(state.saving.value).toBe(false)

    adapters.catalog.unmarkForDeletion.mockRejectedValueOnce(new Error('unmark failed'))
    await persistence.unmarkForDeletion()
    expect(state.error.value).toEqual({ summary: 'unmark failed' })
    expect(state.saving.value).toBe(false)

    adapters.catalog.deleteEntity.mockRejectedValueOnce(new Error('delete failed'))
    await persistence.deleteEntity()
    expect(state.error.value).toEqual({ summary: 'delete failed' })
    expect(state.saving.value).toBe(false)
  })

  it('exposes document effects loading without owning server lifecycle mutations', async () => {
    const { adapters, persistence } = createPersistenceHarness()
    await persistence.loadDocumentEffectsSnapshot('pm.invoice', 'doc-1')
    expect(adapters.document.loadEffectsSnapshot).toHaveBeenCalledWith('pm.invoice', 'doc-1')
    expect(adapters.document).not.toHaveProperty('post')
    expect(adapters.document).not.toHaveProperty('unpost')
    expect(adapters.document).not.toHaveProperty('markForDeletion')

    adapters.document.loadEffectsSnapshot = undefined as never
    await persistence.loadDocumentEffectsSnapshot('pm.invoice', 'doc-2')
  })
})
