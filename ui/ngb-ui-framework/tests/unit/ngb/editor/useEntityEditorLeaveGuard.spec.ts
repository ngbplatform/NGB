import { computed, ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'
import type { Router } from 'vue-router'

import { useEntityEditorLeaveGuard } from '../../../../src/ngb/editor/useEntityEditorLeaveGuard'

function createLeaveGuardHarness() {
  const isDirty = ref(false)
  const loading = ref(false)
  const saving = ref(false)
  const router = {
    push: vi.fn(),
  } as unknown as Router
  const onClose = vi.fn()

  const guard = useEntityEditorLeaveGuard({
    isDirty: computed(() => isDirty.value),
    loading,
    saving,
    router,
    onClose,
  })

  return {
    state: {
      isDirty,
      loading,
      saving,
    },
    router,
    onClose,
    guard,
  }
}

describe('entity editor leave guard', () => {
  it('navigates immediately when the editor is clean or currently busy', () => {
    const { state, router, onClose, guard } = createLeaveGuardHarness()

    guard.requestNavigate('/documents/pm.invoice/doc-1')
    guard.requestClose()

    expect(router.push).toHaveBeenCalledWith('/documents/pm.invoice/doc-1')
    expect(onClose).toHaveBeenCalledTimes(1)

    state.isDirty.value = true
    state.loading.value = true

    guard.requestNavigate('/documents/pm.invoice/doc-2')
    guard.requestClose()

    expect(router.push).toHaveBeenCalledWith('/documents/pm.invoice/doc-2')
    expect(onClose).toHaveBeenCalledTimes(2)
    expect(guard.leaveOpen.value).toBe(false)
  })

  it('opens a leave confirmation before navigation when dirty and resumes on confirm', () => {
    const { state, router, guard } = createLeaveGuardHarness()

    state.isDirty.value = true

    guard.requestNavigate('/documents/pm.invoice/doc-3')

    expect(router.push).not.toHaveBeenCalled()
    expect(guard.leaveOpen.value).toBe(true)

    guard.confirmLeave()

    expect(guard.leaveOpen.value).toBe(false)
    expect(router.push).toHaveBeenCalledWith('/documents/pm.invoice/doc-3')
  })

  it('opens a leave confirmation before closing and clears state on cancel', () => {
    const { state, router, onClose, guard } = createLeaveGuardHarness()

    state.isDirty.value = true

    guard.requestClose()

    expect(guard.leaveOpen.value).toBe(true)
    expect(onClose).not.toHaveBeenCalled()

    guard.cancelLeave()
    expect(guard.leaveOpen.value).toBe(false)

    guard.requestClose()
    guard.confirmLeave()

    expect(router.push).not.toHaveBeenCalled()
    expect(onClose).toHaveBeenCalledTimes(1)
  })
})
