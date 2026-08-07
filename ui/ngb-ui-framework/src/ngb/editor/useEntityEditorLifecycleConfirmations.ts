import { computed, ref, type ComputedRef } from 'vue'

import type { EditorKind } from './types'

type ActionHandler = () => void | Promise<void>

type UseEntityEditorLifecycleConfirmationsArgs = {
  kind: ComputedRef<EditorKind>
  isDirty: ComputedRef<boolean>
  canMarkForDeletion: ComputedRef<boolean>
  canUnpost: ComputedRef<boolean>
  onMarkForDeletion: ActionHandler
  onUnpost: ActionHandler
}

function run(handler: ActionHandler): void {
  void Promise.resolve(handler())
}

export function useEntityEditorLifecycleConfirmations(
  args: UseEntityEditorLifecycleConfirmationsArgs,
) {
  const markConfirmOpen = ref(false)
  const unpostConfirmOpen = ref(false)

  const markConfirmMessage = computed(() => {
    const entity = args.kind.value === 'catalog' ? 'record' : 'document'
    const dirtyWarning = args.isDirty.value ? ' Unsaved changes will be lost.' : ''
    return `This will mark the ${entity} for deletion.${dirtyWarning}`
  })

  function requestMarkForDeletion() {
    if (args.canMarkForDeletion.value) markConfirmOpen.value = true
  }

  function cancelMarkForDeletion() {
    markConfirmOpen.value = false
  }

  function confirmMarkForDeletion() {
    markConfirmOpen.value = false
    run(args.onMarkForDeletion)
  }

  function requestUnpost() {
    if (args.canUnpost.value) unpostConfirmOpen.value = true
  }

  function cancelUnpost() {
    unpostConfirmOpen.value = false
  }

  function confirmUnpost() {
    unpostConfirmOpen.value = false
    run(args.onUnpost)
  }

  return {
    markConfirmOpen,
    markConfirmMessage,
    requestMarkForDeletion,
    cancelMarkForDeletion,
    confirmMarkForDeletion,
    unpostConfirmOpen,
    requestUnpost,
    cancelUnpost,
    confirmUnpost,
  }
}
