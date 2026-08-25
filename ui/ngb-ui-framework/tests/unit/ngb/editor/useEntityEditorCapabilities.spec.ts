import { computed, ref } from 'vue'
import { describe, expect, it } from 'vitest'

import { useEntityEditorCapabilities } from '../../../../src/ngb/editor/useEntityEditorCapabilities'

function createCapabilitiesHarness() {
  const kind = ref<'catalog' | 'document'>('document')
  const currentId = ref<string | null>('doc-1')
  const metadata = ref<{
    displayName?: string | null
    form?: unknown | null
    capabilities?: {
      canCreate?: boolean
      canEdit?: boolean
      canEditDraft?: boolean
      canDelete?: boolean
      canMarkForDeletion?: boolean
      canViewEffects?: boolean
      canViewFlow?: boolean
    } | null
  } | null>({
    displayName: 'Customer Invoice',
    form: {
      sections: [],
    },
  })
  const model = ref<Record<string, unknown>>({
    display: 'Invoice INV-001',
  })
  const loading = ref(false)
  const saving = ref(false)
  const isNew = ref(false)
  const isDraft = ref(true)
  const isMarkedForDeletion = ref(false)
  const status = ref(1)

  const capabilities = useEntityEditorCapabilities({
    kind: computed(() => kind.value),
    currentId,
    metadata: computed(() => metadata.value),
    model,
    loading,
    saving,
    isNew: computed(() => isNew.value),
    isDraft: computed(() => isDraft.value),
    isMarkedForDeletion: computed(() => isMarkedForDeletion.value),
    status: computed(() => status.value),
  })

  return {
    state: {
      kind,
      currentId,
      metadata,
      model,
      loading,
      saving,
      isNew,
      isDraft,
      isMarkedForDeletion,
      status,
    },
    capabilities,
  }
}

describe('entity editor capabilities', () => {
  it('computes draft document capabilities, titles, and audit metadata', () => {
    const { capabilities } = createCapabilitiesHarness()

    expect(capabilities.canOpenAudit.value).toBe(true)
    expect(capabilities.canShareLink.value).toBe(true)
    expect(capabilities.canOpenEffectsPage.value).toBe(true)
    expect(capabilities.canOpenDocumentFlowPage.value).toBe(true)
    expect(capabilities.canPrintDocument.value).toBe(true)
    expect(capabilities.canMarkForDeletion.value).toBe(false)
    expect(capabilities.canUnmarkForDeletion.value).toBe(false)
    expect(capabilities.canDelete.value).toBe(false)
    expect(capabilities.canSave.value).toBe(true)
    expect(capabilities.documentStatusLabel.value).toBe('Draft')
    expect(capabilities.documentStatusTone.value).toBe('neutral')
    expect(capabilities.title.value).toBe('Invoice INV-001')
    expect(capabilities.subtitle.value).toBe('Draft')
    expect(capabilities.auditEntityKind.value).toBe(1)
    expect(capabilities.auditEntityId.value).toBe('doc-1')
    expect(capabilities.auditEntityTitle.value).toBe('Invoice INV-001')
    expect(capabilities.isReadOnly.value).toBe(false)
  })

  it('switches posted and marked documents into read-only restore/unpost semantics', () => {
    const { state, capabilities } = createCapabilitiesHarness()

    state.isDraft.value = false
    state.status.value = 2
    state.isMarkedForDeletion.value = true

    expect(capabilities.canMarkForDeletion.value).toBe(false)
    expect(capabilities.canUnmarkForDeletion.value).toBe(false)
    expect(capabilities.canSave.value).toBe(false)
    expect(capabilities.documentStatusLabel.value).toBe('Posted')
    expect(capabilities.documentStatusTone.value).toBe('success')
    expect(capabilities.isReadOnly.value).toBe(true)
  })

  it('computes catalog titles, subtitles, and delete semantics separately from documents', () => {
    const { state, capabilities } = createCapabilitiesHarness()

    state.kind.value = 'catalog'
    expect(capabilities.documentStatusLabel.value).toBe('Draft')
    expect(capabilities.documentStatusTone.value).toBe('neutral')
    state.currentId.value = 'property-1'
    state.metadata.value = {
      displayName: 'Property',
      form: {
        sections: [],
      },
    }
    state.model.value = {
      display: '',
    }
    state.isNew.value = true
    state.isDraft.value = false
    state.status.value = 3
    expect(capabilities.documentStatusTone.value).toBe('neutral')

    expect(capabilities.canOpenEffectsPage.value).toBe(false)
    expect(capabilities.canOpenDocumentFlowPage.value).toBe(false)
    expect(capabilities.canPrintDocument.value).toBe(false)
    expect(capabilities.canDelete.value).toBe(false)
    expect(capabilities.canMarkForDeletion.value).toBe(false)
    expect(capabilities.canUnmarkForDeletion.value).toBe(false)
    expect(capabilities.canSave.value).toBe(true)
    expect(capabilities.title.value).toBe('New Property')
    expect(capabilities.subtitle.value).toBe('New record')
    expect(capabilities.auditEntityKind.value).toBe(2)
    expect(capabilities.isReadOnly.value).toBe(false)

    state.isNew.value = false
    expect(capabilities.canMarkForDeletion.value).toBe(true)
    state.isMarkedForDeletion.value = true
    expect(capabilities.canMarkForDeletion.value).toBe(false)
    expect(capabilities.canUnmarkForDeletion.value).toBe(true)
  })

  it('honors every explicit capability denial and missing-form boundary', () => {
    const { state, capabilities } = createCapabilitiesHarness()
    state.metadata.value = {
      displayName: null,
      form: null,
      capabilities: {
        canCreate: false,
        canEdit: false,
        canEditDraft: false,
        canDelete: false,
        canMarkForDeletion: false,
        canViewEffects: false,
        canViewFlow: false,
      },
    }
    state.model.value = {}

    expect(capabilities.canOpenEffectsPage.value).toBe(false)
    expect(capabilities.canOpenDocumentFlowPage.value).toBe(false)
    expect(capabilities.canSave.value).toBe(false)
    expect(capabilities.title.value).toBe('Document')
    expect(capabilities.auditEntityTitle.value).toBe('Document')

    state.metadata.value.form = {}
    expect(capabilities.canSave.value).toBe(false)

    state.kind.value = 'catalog'
    expect(capabilities.canMarkForDeletion.value).toBe(false)
    expect(capabilities.canDelete.value).toBe(false)
    expect(capabilities.canSave.value).toBe(false)
    expect(capabilities.title.value).toBe('Catalog record')
    expect(capabilities.subtitle.value).toBeUndefined()

    state.isNew.value = true
    expect(capabilities.canSave.value).toBe(false)
    expect(capabilities.title.value).toBe('New Catalog record')
  })

  it('covers loading, saving, identity, and create/edit state transitions independently', () => {
    const { state, capabilities } = createCapabilitiesHarness()

    state.currentId.value = null
    expect(capabilities.canOpenAudit.value).toBe(false)
    expect(capabilities.canShareLink.value).toBe(false)
    expect(capabilities.canOpenEffectsPage.value).toBe(false)
    expect(capabilities.canOpenDocumentFlowPage.value).toBe(false)
    expect(capabilities.canPrintDocument.value).toBe(false)

    state.currentId.value = 'doc-1'
    state.loading.value = true
    expect(capabilities.canOpenEffectsPage.value).toBe(false)
    expect(capabilities.canOpenDocumentFlowPage.value).toBe(false)
    expect(capabilities.canPrintDocument.value).toBe(false)
    state.loading.value = false
    state.saving.value = true
    expect(capabilities.canOpenEffectsPage.value).toBe(false)
    expect(capabilities.canOpenDocumentFlowPage.value).toBe(false)
    expect(capabilities.canPrintDocument.value).toBe(false)

    state.saving.value = false
    state.isNew.value = true
    state.model.value = {}
    expect(capabilities.canOpenAudit.value).toBe(false)
    expect(capabilities.canShareLink.value).toBe(false)
    expect(capabilities.canOpenEffectsPage.value).toBe(false)
    expect(capabilities.canOpenDocumentFlowPage.value).toBe(false)
    expect(capabilities.canPrintDocument.value).toBe(false)
    expect(capabilities.canSave.value).toBe(true)
    expect(capabilities.title.value).toBe('New Customer Invoice')
    expect(capabilities.documentStatusLabel.value).toBe('Draft')

    state.kind.value = 'catalog'
    state.isNew.value = false
    expect(capabilities.canUnmarkForDeletion.value).toBe(false)
    state.loading.value = true
    expect(capabilities.canMarkForDeletion.value).toBe(false)
    expect(capabilities.canUnmarkForDeletion.value).toBe(false)
    expect(capabilities.canDelete.value).toBe(false)
    state.loading.value = false
    state.saving.value = true
    expect(capabilities.canMarkForDeletion.value).toBe(false)
    expect(capabilities.canUnmarkForDeletion.value).toBe(false)
    expect(capabilities.canDelete.value).toBe(false)

    state.saving.value = false
    state.isMarkedForDeletion.value = false
    expect(capabilities.canDelete.value).toBe(true)
    state.metadata.value = {
      ...state.metadata.value,
      capabilities: { canMarkForDeletion: false },
    }
    state.isMarkedForDeletion.value = true
    expect(capabilities.canUnmarkForDeletion.value).toBe(false)
    state.isMarkedForDeletion.value = false
    state.model.value = { display: 'Riverfront' }
    expect(capabilities.title.value).toBe('Customer Invoice')
    expect(capabilities.subtitle.value).toBe('Riverfront')
  })
})
