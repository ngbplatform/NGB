import { computed, ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'

import { useEntityEditorHeaderActions } from '../../../../src/ngb/editor/useEntityEditorHeaderActions'

function createArgs() {
  const kind = ref<'catalog' | 'document'>('document')
  const mode = ref<'page' | 'drawer'>('page')
  const compactTo = ref<string | null>('/documents/pm.invoice?panel=edit&id=doc-1')
  const expandTo = ref<string | null>('/documents/pm.invoice/doc-1')
  const currentId = ref<string | null>('doc-1')
  const loading = ref(false)
  const saving = ref(false)
  const isNew = ref(false)
  const isMarkedForDeletion = ref(false)
  const canSave = ref(true)
  const canPost = ref(true)
  const canUnpost = ref(false)
  const canMarkForDeletion = ref(true)
  const canUnmarkForDeletion = ref(false)
  const canOpenEffectsPage = ref(true)
  const canOpenDocumentFlowPage = ref(true)
  const canPrintDocument = ref(true)
  const canOpenAudit = ref(true)
  const canShareLink = ref(true)
  const extraPrimaryActions = ref([{ key: 'customPrimary', title: 'Custom primary', icon: 'sparkles' as const }])
  const extraMoreActionGroups = ref([
    {
      key: 'custom',
      label: 'Custom',
      items: [{ key: 'customMore', title: 'Custom more', icon: 'sparkles' as const }],
    },
  ])

  const handlers = {
    onOpenCompactPage: vi.fn(),
    onOpenFullPage: vi.fn(),
    onCopyDocument: vi.fn(),
    onPrintDocument: vi.fn(),
    onToggleMarkForDeletion: vi.fn(),
    onSave: vi.fn(),
    onTogglePost: vi.fn(),
    onOpenEffectsPage: vi.fn(),
    onOpenDocumentFlowPage: vi.fn(),
    onOpenAuditLog: vi.fn(),
    onCopyShareLink: vi.fn(),
    onUnhandledAction: vi.fn(),
  }
  const extraActionHandlers = {
    customMore: vi.fn(),
  }

  return {
    state: {
      kind,
      mode,
      compactTo,
      expandTo,
      currentId,
      loading,
      saving,
      isNew,
      isMarkedForDeletion,
      canSave,
      canPost,
      canUnpost,
      canMarkForDeletion,
      canUnmarkForDeletion,
      canOpenEffectsPage,
      canOpenDocumentFlowPage,
      canPrintDocument,
      canOpenAudit,
      canShareLink,
      extraPrimaryActions,
      extraMoreActionGroups,
    },
    handlers,
    extraActionHandlers,
    args: {
      kind: computed(() => kind.value),
      mode: computed(() => mode.value),
      compactTo: computed(() => compactTo.value),
      expandTo: computed(() => expandTo.value),
      currentId: computed(() => currentId.value),
      loading: computed(() => loading.value),
      saving: computed(() => saving.value),
      isNew: computed(() => isNew.value),
      isMarkedForDeletion: computed(() => isMarkedForDeletion.value),
      canSave: computed(() => canSave.value),
      canPost: computed(() => canPost.value),
      canUnpost: computed(() => canUnpost.value),
      canMarkForDeletion: computed(() => canMarkForDeletion.value),
      canUnmarkForDeletion: computed(() => canUnmarkForDeletion.value),
      canOpenEffectsPage: computed(() => canOpenEffectsPage.value),
      canOpenDocumentFlowPage: computed(() => canOpenDocumentFlowPage.value),
      canPrintDocument: computed(() => canPrintDocument.value),
      canOpenAudit: computed(() => canOpenAudit.value),
      canShareLink: computed(() => canShareLink.value),
      ...handlers,
      extraPrimaryActions: computed(() => extraPrimaryActions.value),
      extraMoreActionGroups: computed(() => extraMoreActionGroups.value),
      extraActionHandlers,
      onUnhandledAction: handlers.onUnhandledAction,
    },
  }
}

describe('entity editor header actions', () => {
  it('builds document header primary actions and grouped more actions for page mode', () => {
    const { args } = createArgs()

    const actions = useEntityEditorHeaderActions(args)

    expect(actions.documentPrimaryActions.value).toEqual([
      {
        key: 'openCompactPage',
        title: 'Open compact page',
        icon: 'panel-right',
        disabled: false,
      },
      {
        key: 'toggleMarkForDeletion',
        title: 'Mark for deletion',
        icon: 'trash',
        disabled: false,
      },
      {
        key: 'save',
        title: 'Save',
        icon: 'save',
        disabled: false,
      },
      {
        key: 'togglePost',
        title: 'Post',
        icon: 'check',
        disabled: false,
      },
      {
        key: 'customPrimary',
        title: 'Custom primary',
        icon: 'sparkles',
      },
    ])

    expect(actions.documentMoreActionGroups.value).toEqual([
      {
        key: 'create',
        label: 'Create',
        items: [{ key: 'copyDocument', title: 'Copy', icon: 'copy', disabled: false }],
      },
      {
        key: 'related-views',
        label: 'Related views',
        items: [
          { key: 'openEffectsPage', title: 'Accounting entries / effects', icon: 'effects-flow', disabled: false },
          { key: 'openDocumentFlowPage', title: 'Document flow', icon: 'document-flow', disabled: false },
        ],
      },
      {
        key: 'output',
        label: 'Output',
        items: [{ key: 'printDocument', title: 'Print', icon: 'printer', disabled: false }],
      },
      {
        key: 'history-and-share',
        label: 'History & share',
        items: [
          { key: 'openAuditLog', title: 'Audit log', icon: 'history', disabled: false },
          { key: 'copyShareLink', title: 'Share link', icon: 'share', disabled: false },
        ],
      },
      {
        key: 'custom',
        label: 'Custom',
        items: [{ key: 'customMore', title: 'Custom more', icon: 'sparkles' }],
      },
    ])
  })

  it('merges create actions after Copy instead of rendering a separate create group', () => {
    const { args, state } = createArgs()
    state.extraMoreActionGroups.value = [
      {
        key: 'create',
        label: 'Create',
        items: [
          { key: 'derive:salesInvoice', title: 'Sales Invoice', icon: 'file-text' as const },
        ],
      },
      {
        key: 'custom',
        label: 'Custom',
        items: [{ key: 'customMore', title: 'Custom more', icon: 'sparkles' as const }],
      },
    ]

    const actions = useEntityEditorHeaderActions(args)

    expect(actions.documentMoreActionGroups.value).toEqual([
      {
        key: 'create',
        label: 'Create',
        items: [
          { key: 'copyDocument', title: 'Copy', icon: 'copy', disabled: false },
          { key: 'derive:salesInvoice', title: 'Sales Invoice', icon: 'file-text' },
        ],
      },
      {
        key: 'related-views',
        label: 'Related views',
        items: [
          { key: 'openEffectsPage', title: 'Accounting entries / effects', icon: 'effects-flow', disabled: false },
          { key: 'openDocumentFlowPage', title: 'Document flow', icon: 'document-flow', disabled: false },
        ],
      },
      {
        key: 'output',
        label: 'Output',
        items: [{ key: 'printDocument', title: 'Print', icon: 'printer', disabled: false }],
      },
      {
        key: 'history-and-share',
        label: 'History & share',
        items: [
          { key: 'openAuditLog', title: 'Audit log', icon: 'history', disabled: false },
          { key: 'copyShareLink', title: 'Share link', icon: 'share', disabled: false },
        ],
      },
      {
        key: 'custom',
        label: 'Custom',
        items: [{ key: 'customMore', title: 'Custom more', icon: 'sparkles' }],
      },
    ])
  })

  it('switches to drawer semantics and restore/unpost labels when editor state changes', () => {
    const { args, state } = createArgs()
    const actions = useEntityEditorHeaderActions(args)

    state.mode.value = 'drawer'
    state.isMarkedForDeletion.value = true
    state.canMarkForDeletion.value = false
    state.canUnmarkForDeletion.value = true
    state.canPost.value = false
    state.canUnpost.value = true

    expect(actions.documentPrimaryActions.value).toEqual([
      {
        key: 'openFullPage',
        title: 'Open full page',
        icon: 'open-in-new',
        disabled: false,
      },
      {
        key: 'toggleMarkForDeletion',
        title: 'Unmark for deletion',
        icon: 'trash-restore',
        disabled: false,
      },
      {
        key: 'save',
        title: 'Restore to edit',
        icon: 'save',
        disabled: false,
      },
      {
        key: 'togglePost',
        title: 'Unpost',
        icon: 'undo',
        disabled: false,
      },
      {
        key: 'customPrimary',
        title: 'Custom primary',
        icon: 'sparkles',
      },
    ])
  })

  it('dispatches built-in, extra, and fallback header actions', () => {
    const { args, handlers, extraActionHandlers } = createArgs()
    const actions = useEntityEditorHeaderActions(args)

    actions.handleDocumentHeaderAction('save')
    actions.handleDocumentHeaderAction('openEffectsPage')
    actions.handleDocumentHeaderAction('customMore')
    actions.handleDocumentHeaderAction('unknownAction')

    expect(handlers.onSave).toHaveBeenCalledTimes(1)
    expect(handlers.onOpenEffectsPage).toHaveBeenCalledTimes(1)
    expect(extraActionHandlers.customMore).toHaveBeenCalledTimes(1)
    expect(handlers.onUnhandledAction).toHaveBeenCalledWith('unknownAction')
  })
})
