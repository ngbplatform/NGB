import { computed, nextTick, ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'

import { useEntityEditorOutputs } from '../../../../src/ngb/editor/useEntityEditorOutputs'

function createOutputsHarness() {
  const title = ref('Invoice INV-001')
  const subtitle = ref<string | undefined>('Draft')
  const isDirty = ref(false)
  const loading = ref(false)
  const saving = ref(false)
  const canExpand = ref(true)
  const canDelete = ref(false)
  const canMarkForDeletion = ref(true)
  const canUnmarkForDeletion = ref(false)
  const canPost = ref(true)
  const canUnpost = ref(false)
  const canOpenAudit = ref(true)
  const canShareLink = ref(true)
  const canSave = ref(true)
  const extraFlags = ref<Record<string, boolean | undefined> | null>({
    hasCustomToolbar: true,
  })
  const emit = vi.fn()

  const outputs = useEntityEditorOutputs({
    emit,
    title: computed(() => title.value),
    subtitle: computed(() => subtitle.value),
    isDirty: computed(() => isDirty.value),
    loading: computed(() => loading.value),
    saving: computed(() => saving.value),
    canExpand: computed(() => canExpand.value),
    canDelete: computed(() => canDelete.value),
    canMarkForDeletion: computed(() => canMarkForDeletion.value),
    canUnmarkForDeletion: computed(() => canUnmarkForDeletion.value),
    canPost: computed(() => canPost.value),
    canUnpost: computed(() => canUnpost.value),
    canOpenAudit: computed(() => canOpenAudit.value),
    canShareLink: computed(() => canShareLink.value),
    canSave: computed(() => canSave.value),
    extraFlags: computed(() => extraFlags.value),
  })

  return {
    state: {
      title,
      subtitle,
      isDirty,
      loading,
      saving,
      canExpand,
      canDelete,
      canMarkForDeletion,
      canUnmarkForDeletion,
      canPost,
      canUnpost,
      canOpenAudit,
      canShareLink,
      canSave,
      extraFlags,
    },
    emit,
    outputs,
  }
}

describe('entity editor outputs', () => {
  it('emits initial state and flag snapshots immediately', () => {
    const { emit, outputs } = createOutputsHarness()

    expect(outputs.flags.value).toEqual({
      canSave: true,
      isDirty: false,
      loading: false,
      saving: false,
      canExpand: true,
      canDelete: false,
      canMarkForDeletion: true,
      canUnmarkForDeletion: false,
      canPost: true,
      canUnpost: false,
      canShowAudit: true,
      canShareLink: true,
      extras: {
        hasCustomToolbar: true,
      },
    })
    expect(emit).toHaveBeenNthCalledWith(1, 'state', {
      title: 'Invoice INV-001',
      subtitle: 'Draft',
    })
    expect(emit).toHaveBeenNthCalledWith(2, 'flags', outputs.flags.value)
  })

  it('re-emits state and flags when reactive values change', async () => {
    const { state, emit } = createOutputsHarness()

    emit.mockClear()
    state.title.value = 'Invoice INV-002'
    state.subtitle.value = 'Posted'
    state.isDirty.value = true
    state.canDelete.value = true
    state.extraFlags.value = {
      hasCustomToolbar: false,
      showPreview: true,
    }

    await nextTick()

    expect(emit).toHaveBeenNthCalledWith(1, 'state', {
      title: 'Invoice INV-002',
      subtitle: 'Posted',
    })
    expect(emit).toHaveBeenNthCalledWith(2, 'flags', {
      canSave: true,
      isDirty: true,
      loading: false,
      saving: false,
      canExpand: true,
      canDelete: true,
      canMarkForDeletion: true,
      canUnmarkForDeletion: false,
      canPost: true,
      canUnpost: false,
      canShowAudit: true,
      canShareLink: true,
      extras: {
        hasCustomToolbar: false,
        showPreview: true,
      },
    })
  })
})
