import { computed, nextTick, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const resolveProfileMock = vi.hoisted(() => vi.fn())
const sanitizeModelForEditingMock = vi.hoisted(() => vi.fn())
const syncComputedDisplayMock = vi.hoisted(() => vi.fn())

vi.mock('../../../../src/ngb/editor/config', async () => {
  const actual = await vi.importActual('../../../../src/ngb/editor/config')
  return {
    ...actual,
    resolveNgbEditorEntityProfile: resolveProfileMock,
    sanitizeNgbEditorModelForEditing: sanitizeModelForEditingMock,
    syncNgbEditorComputedDisplay: syncComputedDisplayMock,
  }
})

import { useEntityEditorBusinessContext } from '../../../../src/ngb/editor/useEntityEditorBusinessContext'

function createHarness() {
  const kind = ref<'catalog' | 'document'>('document')
  const typeCode = ref('pm.invoice')
  const model = ref<Record<string, unknown>>({
    number: 'INV-001',
    customer_id: 'customer-1',
    amount: 1250,
  })
  const docMeta = ref<{ parts?: unknown[] | null } | null>({
    parts: [{ code: 'lines' }],
  })
  const loading = ref(false)
  const isNew = ref(false)
  const isDraft = ref(true)
  const isMarkedForDeletion = ref(false)

  const business = useEntityEditorBusinessContext({
    kind: computed(() => kind.value),
    typeCode: computed(() => typeCode.value),
    model,
    docMeta,
    loading,
    isNew: computed(() => isNew.value),
    isDraft: computed(() => isDraft.value),
    isMarkedForDeletion: computed(() => isMarkedForDeletion.value),
  })

  return {
    state: {
      kind,
      typeCode,
      model,
      docMeta,
      loading,
      isNew,
      isDraft,
      isMarkedForDeletion,
    },
    business,
  }
}

describe('entity editor business context', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('exposes current editor context, deduped profile tags, and document table detection', () => {
    resolveProfileMock.mockReturnValue({
      tags: ['leases', 'leases', 'pm'],
      sanitizeWatchFields: [],
      computedDisplayWatchFields: [],
    })

    const { state, business } = createHarness()

    expect(business.currentEditorContext()).toEqual({
      kind: 'document',
      typeCode: 'pm.invoice',
    })
    expect(business.tags.value).toEqual(['leases', 'pm'])
    expect(business.hasTag('leases')).toBe(true)
    expect(business.hasTag('missing')).toBe(false)
    expect(business.hasDocumentTables.value).toBe(true)

    state.kind.value = 'catalog'
    expect(business.hasDocumentTables.value).toBe(false)
  })

  it('sanitizes the model when watched sanitize fields or context change', async () => {
    resolveProfileMock.mockReturnValue({
      tags: [],
      sanitizeWatchFields: ['number'],
      computedDisplayWatchFields: [],
      sanitizeModelForEditing: () => {},
    })

    const { state } = createHarness()

    state.model.value.amount = 1300
    await nextTick()
    expect(sanitizeModelForEditingMock).not.toHaveBeenCalled()

    state.model.value.number = 'INV-002'
    await nextTick()
    expect(sanitizeModelForEditingMock).toHaveBeenCalledWith(
      { kind: 'document', typeCode: 'pm.invoice' },
      state.model.value,
    )

    state.typeCode.value = 'pm.credit_note'
    await nextTick()
    expect(sanitizeModelForEditingMock).toHaveBeenLastCalledWith(
      { kind: 'document', typeCode: 'pm.credit_note' },
      state.model.value,
    )
  })

  it('syncs computed display only when editor state allows it', async () => {
    resolveProfileMock.mockReturnValue({
      tags: [],
      sanitizeWatchFields: [],
      computedDisplayWatchFields: ['customer_id'],
      computedDisplayMode: 'new_or_draft',
      syncComputedDisplay: () => {},
    })

    const { state } = createHarness()

    state.isDraft.value = false
    state.model.value.customer_id = 'customer-2'
    await nextTick()
    expect(syncComputedDisplayMock).not.toHaveBeenCalled()

    state.isDraft.value = true
    state.model.value.customer_id = 'customer-3'
    await nextTick()
    expect(syncComputedDisplayMock).toHaveBeenCalledWith(
      { kind: 'document', typeCode: 'pm.invoice' },
      state.model.value,
    )

    state.loading.value = true
    state.model.value.customer_id = 'customer-4'
    await nextTick()
    expect(syncComputedDisplayMock).toHaveBeenCalledTimes(1)

    state.loading.value = false
    state.isMarkedForDeletion.value = true
    state.model.value.customer_id = 'customer-5'
    await nextTick()
    expect(syncComputedDisplayMock).toHaveBeenCalledTimes(1)
  })
})
