import { ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  getDocumentEditorState: vi.fn(),
  getDocumentEffects: vi.fn(),
  resetInitialSnapshot: vi.fn(),
}))

vi.mock('@ngbplatform/ui', () => ({
  applyInitialFieldValues: vi.fn(),
  buildDocumentFullPageUrl: vi.fn(),
  buildFieldsPayload: vi.fn(),
  clonePlainData: (value: unknown) => structuredClone(value),
  createDraft: vi.fn(),
  ensureModelKeys: vi.fn(),
  getDocumentEditorState: mocks.getDocumentEditorState,
  getDocumentEffects: mocks.getDocumentEffects,
  hydrateEntityReferenceFieldsForEditing: vi.fn(),
  markDocumentForDeletion: vi.fn(),
  postDocument: vi.fn(),
  resolveNavigateOnCreate: vi.fn(),
  setModelFromFields: (model: { value: unknown }, fields: unknown) => { model.value = fields },
  syncNgbEditorComputedDisplay: vi.fn(),
  unmarkDocumentForDeletion: vi.fn(),
  unpostDocument: vi.fn(),
  updateDraft: vi.fn(),
}))

vi.mock('../../../src/editor/documentParts', () => ({
  buildAgencyBillingDocumentPartsPayload: vi.fn(),
  hydrateAgencyBillingDocumentPartLookupRows: vi.fn(),
  syncAgencyBillingDocumentComputedFields: vi.fn(),
}))

import { useDocumentEntityEditorPersistence } from '../../../src/editor/useDocumentEntityEditorPersistence'

describe('agency-billing document editor persistence', () => {
  it('hydrates an existing editor from the unified editor-state endpoint', async () => {
    const document = {
      id: 'doc-1',
      status: 2,
      isMarkedForDeletion: false,
      payload: {
        fields: { memo: 'loaded' },
        parts: { lines: [{ amount: 10 }] },
      },
    }
    mocks.getDocumentEditorState.mockResolvedValueOnce({
      document,
      documentVersion: 4,
      actions: [],
    })
    const context = {
      typeCode: ref('ab.invoice'),
      currentId: ref('doc-1'),
      isNew: ref(false),
      catalogMeta: ref(null),
      catalogItem: ref(null),
      docMeta: ref(null),
      metadata: ref({ form: {} }),
      doc: ref(null),
      docEffects: ref(null),
      model: ref({}),
      partsModel: ref(null),
      initialParts: ref(null),
      initialFields: ref(null),
      metaStore: {
        ensureDocumentType: vi.fn().mockResolvedValue({ form: {}, parts: [] }),
      },
      lookupStore: {},
      currentEditorContext: vi.fn(() => ({})),
      resetInitialSnapshot: mocks.resetInitialSnapshot,
    }

    await useDocumentEntityEditorPersistence(context as never).load()

    expect(mocks.getDocumentEditorState).toHaveBeenCalledWith('ab.invoice', 'doc-1')
    expect(context.doc.value).toEqual(document)
    expect(context.model.value).toEqual({ memo: 'loaded' })
    expect(context.partsModel.value).toEqual({ lines: [{ amount: 10 }] })
    expect(mocks.getDocumentEffects).not.toHaveBeenCalled()
    expect(mocks.resetInitialSnapshot).toHaveBeenCalledOnce()
  })
})
