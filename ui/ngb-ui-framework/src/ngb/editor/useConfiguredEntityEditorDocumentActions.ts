import { computed, type ComputedRef, type Ref, ref, watch } from 'vue'

import { executeDocumentAction, getDocumentEditorState } from '../api/documents'
import type {
  DocumentActionDto,
  DocumentEditorStateDto,
} from '../api/contracts'
import { coerceNgbIconName } from '../primitives/iconNames'
import {
  resolveNgbDocumentActionTarget,
  resolveNgbEditorRouting,
} from './config'
import type { EditorErrorState } from './entityEditorErrors'
import type { DocumentHeaderActionGroup, DocumentHeaderActionItem, EditorKind } from './types'

type UseConfiguredEntityEditorDocumentActionsArgs = {
  kind: ComputedRef<EditorKind>
  typeCode: ComputedRef<string>
  currentId: ComputedRef<string | null>
  loading: ComputedRef<boolean>
  saving: ComputedRef<boolean>
  requestNavigate: (to: string | null | undefined) => void
  setEditorError: (value: EditorErrorState | null) => void
  normalizeEditorError: (cause: unknown) => EditorErrorState
  applyActionDocument?: (document: DocumentEditorStateDto['document'], actionCode: string) => void | Promise<void>
  reloadDocument?: () => Promise<void>
  loadEditorState?: (documentType: string, id: string) => Promise<DocumentEditorStateDto>
}

const locallyRenderedViewActionCodes = new Set([
  'view_effects',
  'view_flow',
  'view_audit',
  'print',
])

// Document lifecycle controls have a stable, state-driven toolbar contract:
// Draft -> Post + Mark, Posted -> Unpost, Marked -> Unmark. They are rendered
// by useEntityEditorHeaderActions so they keep their fixed positions, icons,
// and platform confirmation dialogs. The action catalog remains the execution
// source on the API, but it must not duplicate or replace those controls.
const locallyRenderedLifecycleActionCodes = new Set([
  'post',
  'unpost',
  'repost',
  'mark_for_deletion',
  'unmark_for_deletion',
])

export function useConfiguredEntityEditorDocumentActions(
  args: UseConfiguredEntityEditorDocumentActionsArgs,
) {
  const unifiedState = ref<DocumentEditorStateDto | null>(null)
  const executingActionCode = ref<string | null>(null)
  const loadEditorState = args.loadEditorState ?? getDocumentEditorState

  watch(
    [args.kind, args.typeCode, args.currentId],
    ([kind, typeCode, documentId], _, onCleanup) => {
      unifiedState.value = null
      if (kind !== 'document' || !documentId) return

      let cancelled = false
      onCleanup(() => { cancelled = true })

      void (async () => {
        try {
          const state = await loadEditorState(typeCode, documentId)
          if (!cancelled) unifiedState.value = state
        } catch (cause) {
          if (!cancelled) args.setEditorError(args.normalizeEditorError(cause))
        }
      })()
    },
    { immediate: true },
  )

  const metadataActions = computed(() =>
    (unifiedState.value?.actions ?? [])
      .filter((action) =>
        !locallyRenderedViewActionCodes.has(action.code)
        && !locallyRenderedLifecycleActionCodes.has(action.code))
      .sort((left, right) => left.order - right.order || left.code.localeCompare(right.code))
      .map((action) => toConfiguredAction(action)))

  function toConfiguredAction(action: DocumentActionDto) {
    const disabled = !action.isAllowed
      || args.loading.value
      || args.saving.value
      || executingActionCode.value !== null
    return {
      item: {
        key: `document-action:${action.code}`,
        title: disabled && action.disabledReasons?.[0]
          ? `${action.label} — ${action.disabledReasons[0].message}`
          : action.label,
        icon: coerceNgbIconName(action.icon, action.executionKind === 'Derivation' ? 'file-text' : 'play'),
        disabled,
      },
      group: action.kind === 'Primary'
        ? null
        : {
            key: action.kind === 'Dangerous' ? 'danger-zone' : action.executionKind === 'Derivation' ? 'create' : 'actions',
            label: action.kind === 'Dangerous' ? 'Danger zone' : action.executionKind === 'Derivation' ? 'Create' : 'Actions',
          },
      run: () => executeUnifiedAction(action),
    }
  }

  async function executeUnifiedAction(action: DocumentActionDto): Promise<void> {
    const documentId = args.currentId.value
    const state = unifiedState.value
    if (!documentId || !state) return

    if (action.executionKind === 'Navigation' || action.executionKind === 'View') {
      const route = action.target
        ? resolveNgbDocumentActionTarget(action.target, { documentType: args.typeCode.value, documentId })
        : null
      if (route) args.requestNavigate(route)
      return
    }

    let reason: string | null = null
    if (action.confirmation?.mode === 'RequireReason') {
      reason = window.prompt(action.confirmation.message, '')?.trim() ?? null
      if (!reason) return
    } else if (action.confirmation?.mode === 'Confirm') {
      if (!window.confirm(action.confirmation.message)) return
    }

    executingActionCode.value = action.code
    try {
      const result = await executeDocumentAction(
        args.typeCode.value,
        documentId,
        action.code,
        { expectedVersion: state.documentVersion, reason },
      )
      unifiedState.value = {
        document: result.document,
        documentVersion: result.documentVersion,
        actions: result.actions,
      }
      await args.applyActionDocument?.(result.document, action.code)

      if (result.createdDocument) {
        const target = action.target
          ? {
              ...action.target,
              parameters: {
                ...action.target.parameters,
                documentId: result.createdDocument.id,
              },
            }
          : null
        const route = target
          ? resolveNgbDocumentActionTarget(target, { documentType: args.typeCode.value, documentId })
          : null
        if (route) args.requestNavigate(route)
        return
      }

      if (!args.applyActionDocument) await args.reloadDocument?.()
    } finally {
      executingActionCode.value = null
    }
  }

  const actions = computed(() => metadataActions.value)

  const extraPrimaryActions = computed<DocumentHeaderActionItem[]>(() =>
    actions.value.filter((action) => !action.group).map((action) => action.item))

  const extraMoreActionGroups = computed<DocumentHeaderActionGroup[]>(() => {
    const buckets = new Map<string, DocumentHeaderActionGroup>()
    for (const action of actions.value) {
      if (!action.group) continue
      const current = buckets.get(action.group.key) ?? {
        key: action.group.key,
        label: action.group.label,
        items: [],
      }
      current.items.push(action.item)
      buckets.set(action.group.key, current)
    }
    return Array.from(buckets.values())
  })

  function handleConfiguredAction(actionKey: string): boolean {
    const match = actions.value.find((action) => action.item.key === actionKey)
    if (!match) return false
    if (match.item.disabled) return true
    args.setEditorError(null)
    void Promise.resolve(match.run()).catch((cause) => {
      args.setEditorError(args.normalizeEditorError(cause))
    })
    return true
  }

  return {
    extraPrimaryActions,
    extraMoreActionGroups,
    handleConfiguredAction,
    hasUnifiedActionState: computed(() => unifiedState.value !== null),
    executingDocumentAction: computed(() => executingActionCode.value !== null),
    refreshDocumentActions: async () => {
      if (args.kind.value !== 'document' || !args.currentId.value) return
      unifiedState.value = await loadEditorState(args.typeCode.value, args.currentId.value)
    },
  }
}
