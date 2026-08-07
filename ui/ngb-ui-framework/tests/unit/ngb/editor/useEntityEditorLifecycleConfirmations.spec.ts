import { computed, ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'

import { useEntityEditorLifecycleConfirmations } from '../../../../src/ngb/editor/useEntityEditorLifecycleConfirmations'

describe('entity editor lifecycle confirmations', () => {
  it('opens only allowed confirmations and delegates confirmed actions', async () => {
    const kind = ref<'catalog' | 'document'>('document')
    const dirty = ref(false)
    const canMark = ref(false)
    const canUnpost = ref(false)
    const mark = vi.fn()
    const unpost = vi.fn()
    const confirmations = useEntityEditorLifecycleConfirmations({
      kind: computed(() => kind.value),
      isDirty: computed(() => dirty.value),
      canMarkForDeletion: computed(() => canMark.value),
      canUnpost: computed(() => canUnpost.value),
      onMarkForDeletion: mark,
      onUnpost: unpost,
    })

    confirmations.requestMarkForDeletion()
    confirmations.requestUnpost()
    expect(confirmations.markConfirmOpen.value).toBe(false)
    expect(confirmations.unpostConfirmOpen.value).toBe(false)

    canMark.value = true
    canUnpost.value = true
    confirmations.requestMarkForDeletion()
    confirmations.requestUnpost()
    expect(confirmations.markConfirmOpen.value).toBe(true)
    expect(confirmations.unpostConfirmOpen.value).toBe(true)

    confirmations.cancelMarkForDeletion()
    confirmations.cancelUnpost()
    expect(confirmations.markConfirmOpen.value).toBe(false)
    expect(confirmations.unpostConfirmOpen.value).toBe(false)

    confirmations.requestMarkForDeletion()
    confirmations.requestUnpost()
    confirmations.confirmMarkForDeletion()
    confirmations.confirmUnpost()
    await Promise.resolve()

    expect(mark).toHaveBeenCalledOnce()
    expect(unpost).toHaveBeenCalledOnce()
  })

  it('builds the platform deletion message from entity type and dirty state', () => {
    const kind = ref<'catalog' | 'document'>('document')
    const dirty = ref(false)
    const confirmations = useEntityEditorLifecycleConfirmations({
      kind: computed(() => kind.value),
      isDirty: computed(() => dirty.value),
      canMarkForDeletion: computed(() => true),
      canUnpost: computed(() => true),
      onMarkForDeletion: () => undefined,
      onUnpost: () => undefined,
    })

    expect(confirmations.markConfirmMessage.value)
      .toBe('This will mark the document for deletion.')

    kind.value = 'catalog'
    dirty.value = true
    expect(confirmations.markConfirmMessage.value)
      .toBe('This will mark the record for deletion. Unsaved changes will be lost.')
  })
})
