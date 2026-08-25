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
      key: 'related-views',
      label: 'Related views',
      items: [
        { key: 'document-action:view_effects', title: 'Accounting entries / effects', icon: 'effects-flow' as const, disabled: false },
        { key: 'document-action:view_flow', title: 'Document flow', icon: 'document-flow' as const, disabled: false },
      ],
    },
    {
      key: 'output',
      label: 'Output',
      items: [{ key: 'document-action:print', title: 'Print', icon: 'printer' as const, disabled: false }],
    },
    {
      key: 'history-and-share',
      label: 'History & share',
      items: [{ key: 'document-action:view_audit', title: 'Audit log', icon: 'history' as const, disabled: false }],
    },
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
    'document-action:view_effects': handlers.onOpenEffectsPage,
    'document-action:view_flow': handlers.onOpenDocumentFlowPage,
    'document-action:print': handlers.onPrintDocument,
    'document-action:view_audit': handlers.onOpenAuditLog,
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
      canShareLink: computed(() => canShareLink.value),
      ...handlers,
      extraPrimaryActions: computed(() => extraPrimaryActions.value),
      extraMoreActionGroups: computed(() => extraMoreActionGroups.value),
      documentLifecycleActions: computed(() => ({
        deletion: canUnmarkForDeletion.value
          ? { key: 'document-action:unmark_for_deletion', title: 'Unmark for deletion', icon: 'trash-restore' as const, disabled: false }
          : canMarkForDeletion.value
            ? { key: 'document-action:mark_for_deletion', title: 'Mark for deletion', icon: 'trash' as const, disabled: false }
            : null,
        posting: canUnpost.value
          ? { key: 'document-action:unpost', title: 'Unpost', icon: 'undo' as const, disabled: false }
          : canPost.value
            ? { key: 'document-action:post', title: 'Post', icon: 'check' as const, disabled: false }
            : null,
      })),
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
        key: 'document-action:mark_for_deletion',
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
        key: 'document-action:post',
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
          { key: 'document-action:view_effects', title: 'Accounting entries / effects', icon: 'effects-flow', disabled: false },
          { key: 'document-action:view_flow', title: 'Document flow', icon: 'document-flow', disabled: false },
        ],
      },
      {
        key: 'output',
        label: 'Output',
        items: [{ key: 'document-action:print', title: 'Print', icon: 'printer', disabled: false }],
      },
      {
        key: 'history-and-share',
        label: 'History & share',
        items: [
          { key: 'document-action:view_audit', title: 'Audit log', icon: 'history', disabled: false },
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
        key: 'history-and-share',
        label: 'History & share',
        items: [
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
        key: 'document-action:unmark_for_deletion',
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
        key: 'document-action:unpost',
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
    actions.handleDocumentHeaderAction('document-action:view_effects')
    actions.handleDocumentHeaderAction('customMore')
    actions.handleDocumentHeaderAction('unknownAction')
    actions.handleDocumentHeaderAction('openCompactPage')
    actions.handleDocumentHeaderAction('openFullPage')
    actions.handleDocumentHeaderAction('copyDocument')
    actions.handleDocumentHeaderAction('copyShareLink')

    expect(handlers.onSave).toHaveBeenCalledTimes(1)
    expect(handlers.onOpenEffectsPage).toHaveBeenCalledTimes(1)
    expect(extraActionHandlers.customMore).toHaveBeenCalledTimes(1)
    expect(handlers.onUnhandledAction).toHaveBeenCalledWith('unknownAction')
    expect(handlers.onOpenCompactPage).toHaveBeenCalledTimes(1)
    expect(handlers.onOpenFullPage).toHaveBeenCalledTimes(1)
    expect(handlers.onCopyDocument).toHaveBeenCalledTimes(1)
    expect(handlers.onCopyShareLink).toHaveBeenCalledTimes(1)
  })

  it('returns no document actions for catalogs and tolerates an unhandled action without a fallback', () => {
    const { args, state } = createArgs()
    state.kind.value = 'catalog'
    const withoutFallback = {
      ...args,
      onUnhandledAction: undefined,
      extraActionHandlers: undefined,
    }
    const actions = useEntityEditorHeaderActions(withoutFallback)

    expect(actions.documentPrimaryActions.value).toEqual([])
    expect(actions.documentMoreActionGroups.value).toEqual([])
    expect(() => actions.handleDocumentHeaderAction('missing')).not.toThrow()
  })

  it('covers absent navigation, lifecycle, extras, and every disabled-state operand', () => {
    const { args, state } = createArgs()
    state.compactTo.value = null
    state.expandTo.value = null
    state.currentId.value = null
    state.canShareLink.value = false
    state.isNew.value = true
    state.isMarkedForDeletion.value = true
    state.canMarkForDeletion.value = false
    state.canPost.value = false
    state.loading.value = true
    state.saving.value = false
    state.canSave.value = false

    const minimalArgs = {
      ...args,
      documentLifecycleActions: undefined,
      extraPrimaryActions: undefined,
      extraMoreActionGroups: undefined,
    }
    const actions = useEntityEditorHeaderActions(minimalArgs)

    expect(actions.documentPrimaryActions.value).toEqual([{
      key: 'save',
      title: 'Save',
      icon: 'save',
      disabled: true,
    }])
    expect(actions.documentMoreActionGroups.value).toEqual([])

    state.loading.value = false
    state.saving.value = true
    state.mode.value = 'drawer'
    state.expandTo.value = '/full'
    state.currentId.value = 'doc-1'
    state.canShareLink.value = true
    expect(actions.documentPrimaryActions.value[0]?.disabled).toBe(true)
    expect(actions.documentMoreActionGroups.value.flatMap((group) => group.items).every((item) => item.disabled === true)).toBe(true)
  })

  it('orders canonical and multiple custom groups deterministically', () => {
    const { args, state } = createArgs()
    state.currentId.value = null
    state.canShareLink.value = false
    state.extraMoreActionGroups.value = [
      { key: 'z-custom', label: 'Z', items: [] },
      { key: 'danger-zone', label: 'Danger', items: [] },
      { key: 'actions', label: 'Actions', items: [] },
      { key: 'a-custom', label: 'A', items: [] },
    ]

    const actions = useEntityEditorHeaderActions(args)
    expect(actions.documentMoreActionGroups.value.map((group) => group.key)).toEqual([
      'actions', 'danger-zone', 'z-custom', 'a-custom',
    ])
  })
})
