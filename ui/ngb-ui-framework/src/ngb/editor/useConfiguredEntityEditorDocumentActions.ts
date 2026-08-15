import { computed, type ComputedRef, ref, watch } from 'vue'

import type {
  DocumentActionDto,
  DocumentEditorStateDto,
} from '../api/contracts'
import { resolveNgbNavigationTarget } from '../navigation/config'
import { coerceNgbIconName } from '../primitives/iconNames'
import { getConfiguredNgbEditor, type DocumentActionsGateway } from './config'
import type { EditorErrorState } from './entityEditorErrors'
import type { DocumentHeaderActionGroup, DocumentHeaderActionItem, EditorKind } from './types'

export type DocumentLifecycleHeaderActions = {
  deletion: DocumentHeaderActionItem | null
  posting: DocumentHeaderActionItem | null
}

export type DocumentActionConfirmationState = {
  actionCode: string
  title: string
  message: string
  confirmLabel: string
  requireReason: boolean
  danger: boolean
  loading: boolean
}

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
  gateway?: DocumentActionsGateway
  localActionHandlers?: Record<string, (() => void | Promise<void>) | undefined>
  beforeExecute?: (
    actionCode: string,
  ) => boolean | { proceed: boolean; refreshState?: boolean } | Promise<boolean | { proceed: boolean; refreshState?: boolean }>
}

const lifecycleActionCodes = new Set([
  'post',
  'unpost',
  'repost',
  'mark_for_deletion',
  'unmark_for_deletion',
])

const standardViewActionCodes = new Set([
  'view_effects',
  'view_flow',
  'view_audit',
  'print',
])

const hiddenProductActionCodes = new Set(['repost'])

const lifecyclePresentation: Record<string, Pick<DocumentHeaderActionItem, 'title' | 'icon'>> = {
  post: { title: 'Post', icon: 'check' },
  unpost: { title: 'Unpost', icon: 'undo' },
  mark_for_deletion: { title: 'Mark for deletion', icon: 'trash' },
  unmark_for_deletion: { title: 'Unmark for deletion', icon: 'trash-restore' },
}

const viewPresentation: Record<string, {
  title: string
  icon: DocumentHeaderActionItem['icon']
  group: Pick<DocumentHeaderActionGroup, 'key' | 'label'>
}> = {
  view_effects: {
    title: 'Accounting entries / effects',
    icon: 'effects-flow',
    group: { key: 'related-views', label: 'Related views' },
  },
  view_flow: {
    title: 'Document flow',
    icon: 'document-flow',
    group: { key: 'related-views', label: 'Related views' },
  },
  view_audit: {
    title: 'Audit log',
    icon: 'history',
    group: { key: 'history-and-share', label: 'History & share' },
  },
  print: {
    title: 'Print',
    icon: 'printer',
    group: { key: 'output', label: 'Output' },
  },
}

function firstAllowed(
  actions: DocumentActionDto[],
  codes: readonly string[],
): DocumentActionDto | null {
  for (const code of codes) {
    const action = actions.find((candidate) => candidate.code === code && candidate.isAllowed)
    if (action) return action
  }
  return null
}

export function useConfiguredEntityEditorDocumentActions(
  args: UseConfiguredEntityEditorDocumentActionsArgs,
) {
  const unifiedState = ref<DocumentEditorStateDto | null>(null)
  const executingActionCode = ref<string | null>(null)
  const pendingConfirmationAction = ref<DocumentActionDto | null>(null)
  const gateway = args.gateway ?? getConfiguredNgbEditor().documentActions

  watch(
    [args.kind, args.typeCode, args.currentId],
    ([kind, typeCode, documentId], _, onCleanup) => {
      unifiedState.value = null
      pendingConfirmationAction.value = null
      if (kind !== 'document' || !documentId) return

      let cancelled = false
      onCleanup(() => { cancelled = true })

      void (async () => {
        try {
          const state = await gateway.loadEditorState(typeCode, documentId)
          if (!cancelled) unifiedState.value = state
        } catch (cause) {
          if (!cancelled) args.setEditorError(args.normalizeEditorError(cause))
        }
      })()
    },
    { immediate: true },
  )

  const busy = computed(() =>
    args.loading.value || args.saving.value || executingActionCode.value !== null)

  function toItem(action: DocumentActionDto): DocumentHeaderActionItem {
    const visual = lifecyclePresentation[action.code] ?? viewPresentation[action.code]
    const disabled = !action.isAllowed || busy.value
    return {
      key: `document-action:${action.code}`,
      title: disabled && action.disabledReasons?.[0]
        ? `${visual?.title ?? action.label} — ${action.disabledReasons[0].message}`
        : visual?.title ?? action.label,
      icon: visual?.icon
        ?? coerceNgbIconName(action.icon, action.executionKind === 'Derivation' ? 'file-text' : 'play'),
      disabled,
    }
  }

  const documentLifecycleActions = computed<DocumentLifecycleHeaderActions>(() => {
    const actions = unifiedState.value?.actions ?? []
    const deletion = firstAllowed(actions, ['unmark_for_deletion', 'mark_for_deletion'])
    const posting = firstAllowed(actions, ['unpost', 'post'])
    return {
      deletion: deletion ? toItem(deletion) : null,
      posting: posting ? toItem(posting) : null,
    }
  })

  const projectedActions = computed(() =>
    (unifiedState.value?.actions ?? [])
      .filter((action) => !hiddenProductActionCodes.has(action.code))
      .filter((action) => !lifecycleActionCodes.has(action.code))
      .filter((action) => !standardViewActionCodes.has(action.code) || action.isAllowed)
      .sort((left, right) => left.order - right.order || left.code.localeCompare(right.code))
      .map((action) => {
        const view = viewPresentation[action.code]
        return {
          action,
          item: toItem(action),
          group: view?.group ?? (action.kind === 'Primary'
            ? null
            : {
                key: action.kind === 'Dangerous'
                  ? 'danger-zone'
                  : action.executionKind === 'Derivation' ? 'create' : 'actions',
                label: action.kind === 'Dangerous'
                  ? 'Danger zone'
                  : action.executionKind === 'Derivation' ? 'Create' : 'Actions',
              }),
        }
      }))

  async function executeAction(action: DocumentActionDto, reason: string | null): Promise<void> {
    const documentId = args.currentId.value
    if (!documentId || !unifiedState.value || busy.value || !action.isAllowed) return

    const localHandler = args.localActionHandlers?.[action.code]
    if (localHandler) {
      await localHandler()
      return
    }

    if (action.executionKind === 'Navigation' || action.executionKind === 'View') {
      const route = action.target
        ? resolveNgbNavigationTarget(action.target, {
            resourceKind: 'document',
            resourceCode: args.typeCode.value,
            entityId: documentId,
          })
        : null
      if (route) args.requestNavigate(route)
      return
    }

    executingActionCode.value = action.code
    try {
      const preparation = await args.beforeExecute?.(action.code) ?? true
      const proceed = typeof preparation === 'boolean' ? preparation : preparation.proceed
      if (!proceed) return
      const state = typeof preparation === 'object' && preparation.refreshState
        ? await gateway.loadEditorState(args.typeCode.value, documentId)
        : unifiedState.value
      if (!state) return
      unifiedState.value = state
      const refreshedAction = state.actions.find((candidate) => candidate.code === action.code)
      if (!refreshedAction?.isAllowed) return
      const result = await gateway.execute(
        args.typeCode.value,
        documentId,
        refreshedAction.code,
        { expectedVersion: state.documentVersion, reason },
      )
      unifiedState.value = {
        document: result.document,
        documentVersion: result.documentVersion,
        actions: result.actions,
      }
      await args.applyActionDocument?.(result.document, refreshedAction.code)

      if (result.createdDocument) {
        const target = refreshedAction.target
          ? {
              ...refreshedAction.target,
              parameters: {
                ...refreshedAction.target.parameters,
                documentId: result.createdDocument.id,
              },
            }
          : null
        const route = target
          ? resolveNgbNavigationTarget(target, {
              resourceKind: 'document',
              resourceCode: args.typeCode.value,
              entityId: documentId,
            })
          : null
        if (route) args.requestNavigate(route)
        return
      }

      if (!args.applyActionDocument) await args.reloadDocument?.()
    } finally {
      executingActionCode.value = null
    }
  }

  async function executeAndCapture(action: DocumentActionDto, reason: string | null): Promise<void> {
    args.setEditorError(null)
    try {
      await executeAction(action, reason)
    } catch (cause) {
      args.setEditorError(args.normalizeEditorError(cause))
    }
  }

  function requestAction(action: DocumentActionDto): void {
    if (!action.isAllowed || busy.value) return
    if (action.confirmation && action.confirmation.mode !== 'None') {
      pendingConfirmationAction.value = action
      return
    }
    void executeAndCapture(action, null)
  }

  function actionByCode(actionCode: string): DocumentActionDto | null {
    return unifiedState.value?.actions.find((action) => action.code === actionCode) ?? null
  }

  function requestDocumentAction(actionCode: string): boolean {
    const action = actionByCode(actionCode)
    if (!action || hiddenProductActionCodes.has(action.code)) return false
    requestAction(action)
    return true
  }

  const extraPrimaryActions = computed<DocumentHeaderActionItem[]>(() =>
    projectedActions.value.filter((entry) => !entry.group).map((entry) => entry.item))

  const extraMoreActionGroups = computed<DocumentHeaderActionGroup[]>(() => {
    const buckets = new Map<string, DocumentHeaderActionGroup>()
    for (const entry of projectedActions.value) {
      if (!entry.group) continue
      const current = buckets.get(entry.group.key) ?? {
        key: entry.group.key,
        label: entry.group.label,
        items: [],
      }
      current.items.push(entry.item)
      buckets.set(entry.group.key, current)
    }
    return Array.from(buckets.values())
  })

  function handleConfiguredAction(actionKey: string): boolean {
    if (!actionKey.startsWith('document-action:')) return false
    return requestDocumentAction(actionKey.slice('document-action:'.length))
  }

  const confirmation = computed<DocumentActionConfirmationState | null>(() => {
    const action = pendingConfirmationAction.value
    const metadata = action?.confirmation
    if (!action || !metadata || metadata.mode === 'None') return null
    return {
      actionCode: action.code,
      title: metadata.title,
      message: metadata.message,
      confirmLabel: metadata.confirmLabel,
      requireReason: metadata.mode === 'RequireReason',
      danger: action.kind === 'Dangerous',
      loading: executingActionCode.value === action.code,
    }
  })

  function cancelDocumentActionConfirmation(): void {
    if (executingActionCode.value === null) pendingConfirmationAction.value = null
  }

  async function confirmDocumentAction(reason: string | null = null): Promise<void> {
    const action = pendingConfirmationAction.value
    if (!action) return
    const normalizedReason = String(reason ?? '').trim() || null
    if (action.confirmation?.mode === 'RequireReason' && !normalizedReason) return
    try {
      await executeAndCapture(action, normalizedReason)
    } finally {
      pendingConfirmationAction.value = null
    }
  }

  return {
    documentLifecycleActions,
    extraPrimaryActions,
    extraMoreActionGroups,
    handleConfiguredAction,
    requestDocumentAction,
    isDocumentActionAllowed: (actionCode: string): boolean => !!actionByCode(actionCode)?.isAllowed,
    confirmation,
    cancelDocumentActionConfirmation,
    confirmDocumentAction,
    hasUnifiedActionState: computed(() => unifiedState.value !== null),
    executingDocumentAction: computed(() => executingActionCode.value !== null),
    refreshDocumentActions: async () => {
      if (args.kind.value !== 'document' || !args.currentId.value) return
      unifiedState.value = await gateway.loadEditorState(args.typeCode.value, args.currentId.value)
    },
  }
}
