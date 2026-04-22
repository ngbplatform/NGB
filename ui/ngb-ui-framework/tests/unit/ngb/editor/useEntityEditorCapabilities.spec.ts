import { computed, ref } from 'vue'
import { describe, expect, it } from 'vitest'

import { useEntityEditorCapabilities } from '../../../../src/ngb/editor/useEntityEditorCapabilities'

function createCapabilitiesHarness() {
  const kind = ref<'catalog' | 'document'>('document')
  const currentId = ref<string | null>('doc-1')
  const metadata = ref<{ displayName?: string | null; form?: unknown | null } | null>({
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
    expect(capabilities.canMarkForDeletion.value).toBe(true)
    expect(capabilities.canUnmarkForDeletion.value).toBe(false)
    expect(capabilities.canDelete.value).toBe(false)
    expect(capabilities.canPost.value).toBe(true)
    expect(capabilities.canUnpost.value).toBe(false)
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
    expect(capabilities.canUnmarkForDeletion.value).toBe(true)
    expect(capabilities.canPost.value).toBe(false)
    expect(capabilities.canUnpost.value).toBe(true)
    expect(capabilities.canSave.value).toBe(false)
    expect(capabilities.documentStatusLabel.value).toBe('Posted')
    expect(capabilities.documentStatusTone.value).toBe('success')
    expect(capabilities.isReadOnly.value).toBe(true)
  })

  it('computes catalog titles, subtitles, and delete semantics separately from documents', () => {
    const { state, capabilities } = createCapabilitiesHarness()

    state.kind.value = 'catalog'
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

    expect(capabilities.canOpenEffectsPage.value).toBe(false)
    expect(capabilities.canOpenDocumentFlowPage.value).toBe(false)
    expect(capabilities.canPrintDocument.value).toBe(false)
    expect(capabilities.canDelete.value).toBe(false)
    expect(capabilities.canSave.value).toBe(true)
    expect(capabilities.title.value).toBe('New Property')
    expect(capabilities.subtitle.value).toBe('New record')
    expect(capabilities.auditEntityKind.value).toBe(2)
    expect(capabilities.isReadOnly.value).toBe(false)
  })
})
