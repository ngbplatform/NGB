import { computed, ref } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import type { DocumentEditorStateDto } from '../../../../src/ngb/api/contracts'
import { configureNgbEditor } from '../../../../src/ngb/editor/config'
import { useConfiguredEntityEditorDocumentActions } from '../../../../src/ngb/editor/useConfiguredEntityEditorDocumentActions'

const executeDocumentActionMock = vi.fn()

const emptyState: DocumentEditorStateDto = {
  document: {
    id: 'doc-1',
    display: 'Invoice INV-001',
    payload: { fields: {} },
    status: 1,
    isMarkedForDeletion: false,
  },
  documentVersion: 7,
  actions: [],
}

function createArgs(state: DocumentEditorStateDto = emptyState) {
  const kind = ref<'catalog' | 'document'>('document')
  const typeCode = ref('pm.invoice')
  const currentId = ref<string | null>('doc-1')
  const loading = ref(false)
  const saving = ref(false)
  const requestNavigate = vi.fn()
  const setEditorError = vi.fn()
  const normalizeEditorError = vi.fn((cause: unknown) => ({
    summary: cause instanceof Error ? cause.message : 'normalized',
    issues: [],
  }))
  const applyActionDocument = vi.fn()
  const reloadDocument = vi.fn()
  const loadEditorState = vi.fn().mockResolvedValue(state)

  return {
    kind,
    typeCode,
    currentId,
    loading,
    saving,
    requestNavigate,
    setEditorError,
    normalizeEditorError,
    applyActionDocument,
    reloadDocument,
    loadEditorState,
    args: {
      kind: computed(() => kind.value),
      typeCode: computed(() => typeCode.value),
      currentId: computed(() => currentId.value),
      loading: computed(() => loading.value),
      saving: computed(() => saving.value),
      requestNavigate,
      setEditorError,
      normalizeEditorError,
      applyActionDocument,
      reloadDocument,
      gateway: {
        loadEditorState,
        execute: executeDocumentActionMock,
      },
    },
  }
}

async function flushAsyncWork() {
  await new Promise((resolve) => setTimeout(resolve, 0))
}

describe('configured entity editor document actions', () => {
  beforeEach(() => {
    executeDocumentActionMock.mockReset()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('projects server metadata into primary and grouped header actions', async () => {
    const state: DocumentEditorStateDto = {
      ...emptyState,
      actions: [
        {
          code: 'approve',
          label: 'Approve',
          icon: 'check',
          kind: 'Primary',
          executionKind: 'Command',
          order: 100,
          isAllowed: true,
          disabledReasons: [],
        },
        {
          code: 'email',
          label: 'Email',
          icon: 'mail',
          kind: 'Secondary',
          executionKind: 'Command',
          order: 200,
          isAllowed: false,
          disabledReasons: [{ code: 'email.unavailable', message: 'Recipient is missing.' }],
        },
      ],
    }

    const { args } = createArgs(state)
    const actions = useConfiguredEntityEditorDocumentActions(args)
    await flushAsyncWork()

    expect(actions.hasUnifiedActionState.value).toBe(true)
    expect(actions.extraPrimaryActions.value).toEqual([
      { key: 'document-action:approve', title: 'Approve', icon: 'check', disabled: false },
    ])
    expect(actions.extraMoreActionGroups.value).toEqual([
      {
        key: 'actions',
        label: 'Actions',
        items: [{
          key: 'document-action:email',
          title: 'Email — Recipient is missing.',
          icon: 'play',
          disabled: true,
        }],
      },
    ])
    expect(actions.handleConfiguredAction('missing')).toBe(false)
    expect(actions.handleConfiguredAction('document-action:email')).toBe(true)
    expect(executeDocumentActionMock).not.toHaveBeenCalled()
  })

  it('uses the configured gateway and exposes an empty lifecycle state before and after loading', async () => {
    const state = {
      ...emptyState,
      actions: [{
        code: 'post',
        label: 'Post',
        icon: 'check',
        kind: 'Primary' as const,
        executionKind: 'Command' as const,
        order: 1,
        isAllowed: false,
        disabledReasons: [{ code: 'blocked', message: 'Blocked.' }],
      }, {
        code: 'mark_for_deletion',
        label: 'Mark for deletion',
        icon: 'trash',
        kind: 'Dangerous' as const,
        executionKind: 'Command' as const,
        order: 2,
        isAllowed: false,
        disabledReasons: [{ code: 'blocked', message: 'Blocked.' }],
      }],
    } satisfies DocumentEditorStateDto
    const values = createArgs(state)
    configureNgbEditor({
      loadDocumentById: vi.fn(),
      loadDocumentEffects: vi.fn(),
      loadDocumentGraph: vi.fn(),
      loadEntityAuditLog: vi.fn(),
      documentActions: {
        loadEditorState: values.loadEditorState,
        execute: executeDocumentActionMock,
      },
    })
    delete (values.args as Partial<typeof values.args>).gateway

    const actions = useConfiguredEntityEditorDocumentActions(values.args)
    expect(actions.documentLifecycleActions.value).toEqual({ deletion: null, posting: null })
    expect(actions.isDocumentActionAllowed('post')).toBe(false)
    expect(actions.requestDocumentAction('missing')).toBe(false)
    await flushAsyncWork()

    expect(actions.documentLifecycleActions.value).toEqual({ deletion: null, posting: null })
    expect(actions.isDocumentActionAllowed('post')).toBe(false)
    expect(values.loadEditorState).toHaveBeenCalledWith('pm.invoice', 'doc-1')
  })

  it('executes configured local handlers without invoking the API', async () => {
    const state = {
      ...emptyState,
      actions: [{
        code: 'view_effects',
        label: 'Effects',
        icon: 'eye',
        kind: 'Secondary' as const,
        executionKind: 'View' as const,
        order: 1,
        isAllowed: true,
        disabledReasons: [],
      }],
    } satisfies DocumentEditorStateDto
    const values = createArgs(state)
    const localHandler = vi.fn().mockResolvedValue(undefined)
    const actions = useConfiguredEntityEditorDocumentActions({
      ...values.args,
      localActionHandlers: { view_effects: localHandler },
    })
    await flushAsyncWork()

    expect(actions.requestDocumentAction('view_effects')).toBe(true)
    await flushAsyncWork()
    expect(localHandler).toHaveBeenCalledOnce()
    expect(executeDocumentActionMock).not.toHaveBeenCalled()
  })

  it('honors preparation outcomes and revalidates refreshed action state', async () => {
    const command = {
      code: 'approve',
      label: 'Approve',
      icon: 'check',
      kind: 'Primary' as const,
      executionKind: 'Command' as const,
      order: 1,
      isAllowed: true,
      disabledReasons: [],
    }
    const state = { ...emptyState, actions: [command] } satisfies DocumentEditorStateDto

    const stopped = createArgs(state)
    const stoppedActions = useConfiguredEntityEditorDocumentActions({
      ...stopped.args,
      beforeExecute: vi.fn().mockResolvedValue(false),
    })
    await flushAsyncWork()
    stoppedActions.requestDocumentAction('approve')
    await flushAsyncWork()
    expect(executeDocumentActionMock).not.toHaveBeenCalled()

    const missingState = createArgs(state)
    missingState.loadEditorState
      .mockResolvedValueOnce(state)
      .mockResolvedValueOnce(null)
    const missingStateActions = useConfiguredEntityEditorDocumentActions({
      ...missingState.args,
      beforeExecute: vi.fn().mockResolvedValue({ proceed: true, refreshState: true }),
    })
    await flushAsyncWork()
    missingStateActions.requestDocumentAction('approve')
    await flushAsyncWork()
    expect(executeDocumentActionMock).not.toHaveBeenCalled()

    const disallowed = createArgs(state)
    disallowed.loadEditorState
      .mockResolvedValueOnce(state)
      .mockResolvedValueOnce({ ...state, actions: [{ ...command, isAllowed: false }] })
    const disallowedActions = useConfiguredEntityEditorDocumentActions({
      ...disallowed.args,
      beforeExecute: vi.fn().mockResolvedValue({ proceed: true, refreshState: true }),
    })
    await flushAsyncWork()
    disallowedActions.requestDocumentAction('approve')
    await flushAsyncWork()
    expect(executeDocumentActionMock).not.toHaveBeenCalled()

    executeDocumentActionMock.mockResolvedValueOnce({
      executionId: 'execution',
      actionCode: 'approve',
      document: emptyState.document,
      documentVersion: 8,
      actions: [],
      workCenterMayChange: false,
      createdDocument: null,
    })
    const ready = createArgs(state)
    const readyActions = useConfiguredEntityEditorDocumentActions({
      ...ready.args,
      beforeExecute: vi.fn().mockResolvedValue({ proceed: true, refreshState: false }),
    })
    await flushAsyncWork()
    readyActions.requestDocumentAction('approve')
    await flushAsyncWork()
    expect(executeDocumentActionMock).toHaveBeenCalledOnce()
  })

  it('executes a derivation with optimistic concurrency, applies the returned document, and navigates', async () => {
    const state: DocumentEditorStateDto = {
      ...emptyState,
      actions: [{
        code: 'crm.create_qualification',
        label: 'Create qualification',
        icon: 'file-plus',
        kind: 'Secondary',
        executionKind: 'Derivation',
        order: 500,
        isAllowed: true,
        disabledReasons: [],
        target: {
          code: 'document.editor',
          parameters: {
            documentType: 'crm.lead_qualification',
            documentId: '{createdDocumentId}',
          },
        },
      }],
    }
    const resultDocument = {
      ...emptyState.document,
      status: 2,
    }
    executeDocumentActionMock.mockResolvedValueOnce({
      executionId: 'execution-1',
      actionCode: 'crm.create_qualification',
      document: resultDocument,
      documentVersion: 8,
      actions: [],
      workCenterMayChange: true,
      createdDocument: {
        id: 'qualification-1',
        display: 'Qualification Q-001',
        payload: { fields: {} },
        status: 1,
        isMarkedForDeletion: false,
      },
    })

    const { args, applyActionDocument, requestNavigate, reloadDocument } = createArgs(state)
    const actions = useConfiguredEntityEditorDocumentActions(args)
    await flushAsyncWork()

    expect(actions.extraMoreActionGroups.value[0]?.items[0]?.key)
      .toBe('document-action:crm.create_qualification')
    expect(actions.handleConfiguredAction('document-action:crm.create_qualification')).toBe(true)
    expect(actions.handleConfiguredAction('document-action:crm.create_qualification')).toBe(true)
    await flushAsyncWork()

    expect(executeDocumentActionMock).toHaveBeenCalledTimes(1)
    expect(executeDocumentActionMock).toHaveBeenCalledWith(
      'pm.invoice',
      'doc-1',
      'crm.create_qualification',
      { expectedVersion: 7, reason: null },
    )
    expect(applyActionDocument).toHaveBeenCalledWith(resultDocument, 'crm.create_qualification')
    expect(reloadDocument).not.toHaveBeenCalled()
    expect(requestNavigate).toHaveBeenCalledWith('/documents/crm.lead_qualification/qualification-1')
    expect(actions.executingDocumentAction.value).toBe(false)
  })

  it('normalizes initial-load and execution failures into editor errors', async () => {
    const loadFailure = new Error('Editor state failed')
    const failedLoad = createArgs()
    failedLoad.loadEditorState.mockRejectedValueOnce(loadFailure)
    useConfiguredEntityEditorDocumentActions(failedLoad.args)
    await flushAsyncWork()

    expect(failedLoad.normalizeEditorError).toHaveBeenCalledWith(loadFailure)
    expect(failedLoad.setEditorError).toHaveBeenCalledWith({
      summary: loadFailure.message,
      issues: [],
    })

    const state: DocumentEditorStateDto = {
      ...emptyState,
      actions: [{
        code: 'approve',
        label: 'Approve',
        icon: 'check',
        kind: 'Primary',
        executionKind: 'Command',
        order: 100,
        isAllowed: true,
        disabledReasons: [],
      }],
    }
    const executionFailure = new Error('Version conflict')
    executeDocumentActionMock.mockRejectedValueOnce(executionFailure)
    const execution = createArgs(state)
    const actions = useConfiguredEntityEditorDocumentActions(execution.args)
    await flushAsyncWork()

    expect(actions.handleConfiguredAction('document-action:approve')).toBe(true)
    await flushAsyncWork()
    expect(execution.setEditorError).toHaveBeenNthCalledWith(1, null)
    expect(execution.normalizeEditorError).toHaveBeenCalledWith(executionFailure)
    expect(execution.setEditorError).toHaveBeenNthCalledWith(2, {
      summary: executionFailure.message,
      issues: [],
    })
  })

  it('does not load actions for catalogs or missing document ids', async () => {
    const catalog = createArgs()
    catalog.kind.value = 'catalog'
    const catalogActions = useConfiguredEntityEditorDocumentActions(catalog.args)
    await flushAsyncWork()
    expect(catalog.loadEditorState).not.toHaveBeenCalled()
    expect(catalogActions.extraPrimaryActions.value).toEqual([])

    const missingId = createArgs()
    missingId.currentId.value = null
    const missingIdActions = useConfiguredEntityEditorDocumentActions(missingId.args)
    await flushAsyncWork()
    expect(missingId.loadEditorState).not.toHaveBeenCalled()
    expect(missingIdActions.extraMoreActionGroups.value).toEqual([])
  })

  it('uses the platform loader, ignores stale loads, filters local view actions, and refreshes', async () => {
    let resolveFirst!: (state: DocumentEditorStateDto) => void
    const first = new Promise<DocumentEditorStateDto>((resolve) => { resolveFirst = resolve })
    const latest = {
      ...emptyState,
      documentVersion: 9,
      actions: [{
        code: 'danger',
        label: 'Danger',
        icon: '',
        kind: 'Dangerous',
        executionKind: 'Command',
        order: 300,
        isAllowed: true,
        disabledReasons: [],
      }, {
        code: 'alpha',
        label: 'Alpha',
        icon: 'play',
        kind: 'Secondary',
        executionKind: 'Command',
        order: 300,
        isAllowed: true,
        disabledReasons: [],
      }, {
        code: 'view_effects',
        label: 'Effects',
        icon: 'eye',
        kind: 'Secondary',
        executionKind: 'View',
        order: 1,
        isAllowed: true,
        disabledReasons: [],
      }],
    } satisfies DocumentEditorStateDto
    const values = createArgs()
    values.loadEditorState
      .mockReturnValueOnce(first)
      .mockResolvedValueOnce(latest)
      .mockResolvedValueOnce({ ...latest, documentVersion: 10 })

    const actions = useConfiguredEntityEditorDocumentActions(values.args)
    values.currentId.value = 'doc-2'
    await flushAsyncWork()
    resolveFirst(emptyState)
    await flushAsyncWork()

    expect(actions.extraMoreActionGroups.value).toEqual([
      {
        key: 'related-views',
        label: 'Related views',
        items: [{
          key: 'document-action:view_effects',
          title: 'Accounting entries / effects',
          icon: 'effects-flow',
          disabled: false,
        }],
      },
      {
        key: 'actions',
        label: 'Actions',
        items: [{
          key: 'document-action:alpha',
          title: 'Alpha',
          icon: 'play',
          disabled: false,
        }],
      },
      {
        key: 'danger-zone',
        label: 'Danger zone',
        items: [{
          key: 'document-action:danger',
          title: 'Danger',
          icon: 'play',
          disabled: false,
        }],
      },
    ])
    await actions.refreshDocumentActions()
    expect(values.loadEditorState).toHaveBeenLastCalledWith('pm.invoice', 'doc-2')

    values.kind.value = 'catalog'
    await flushAsyncWork()
    await actions.refreshDocumentActions()
    values.kind.value = 'document'
    values.currentId.value = null
    await flushAsyncWork()
    await actions.refreshDocumentActions()
    expect(values.loadEditorState).toHaveBeenCalledTimes(3)
  })

  it('suppresses an initial-load failure after the watched document changes', async () => {
    let rejectStale!: (cause: unknown) => void
    const stale = new Promise<DocumentEditorStateDto>((_, reject) => { rejectStale = reject })
    const values = createArgs()
    values.loadEditorState
      .mockReturnValueOnce(stale)
      .mockResolvedValueOnce(emptyState)
    useConfiguredEntityEditorDocumentActions(values.args)

    values.currentId.value = 'doc-2'
    await flushAsyncWork()
    rejectStale(new Error('stale failure'))
    await flushAsyncWork()

    expect(values.setEditorError).not.toHaveBeenCalled()
  })

  it('navigates view actions only when a target resolves', async () => {
    const state = {
      ...emptyState,
      actions: [{
        code: 'open',
        label: 'Open',
        icon: 'external-link',
        kind: 'Secondary',
        executionKind: 'Navigation',
        order: 1,
        isAllowed: true,
        disabledReasons: [],
        target: {
          code: 'document.flow',
          parameters: {},
        },
      }, {
        code: 'no-target',
        label: 'No target',
        icon: 'external-link',
        kind: 'Secondary',
        executionKind: 'Navigation',
        order: 2,
        isAllowed: true,
        disabledReasons: [],
      }, {
        code: 'unknown-target',
        label: 'Unknown target',
        icon: 'external-link',
        kind: 'Secondary',
        executionKind: 'View',
        order: 3,
        isAllowed: true,
        disabledReasons: [],
        target: {
          code: 'unknown',
          parameters: {},
        },
      }],
    } satisfies DocumentEditorStateDto
    const values = createArgs(state)
    const actions = useConfiguredEntityEditorDocumentActions(values.args)
    await flushAsyncWork()

    for (const key of ['open', 'no-target', 'unknown-target']) {
      expect(actions.handleConfiguredAction(`document-action:${key}`)).toBe(true)
    }
    await flushAsyncWork()

    expect(values.requestNavigate).toHaveBeenCalledTimes(1)
    expect(values.requestNavigate).toHaveBeenCalledWith('/documents/pm.invoice/doc-1/flow')
    expect(executeDocumentActionMock).not.toHaveBeenCalled()
  })

  it('enforces confirm and required-reason contracts before commands', async () => {
    const state = {
      ...emptyState,
      actions: [{
        code: 'confirm',
        label: 'Confirm',
        icon: 'check',
        kind: 'Primary',
        executionKind: 'Command',
        order: 1,
        isAllowed: true,
        disabledReasons: [],
        confirmation: {
          mode: 'Confirm',
          title: 'Confirm action?',
          message: 'Continue?',
          confirmLabel: 'Confirm',
        },
      }, {
        code: 'reason',
        label: 'Reason',
        icon: 'check',
        kind: 'Primary',
        executionKind: 'Command',
        order: 2,
        isAllowed: true,
        disabledReasons: [],
        confirmation: {
          mode: 'RequireReason',
          title: 'Reason required',
          message: 'Why?',
          confirmLabel: 'Continue',
        },
      }],
    } satisfies DocumentEditorStateDto
    executeDocumentActionMock.mockResolvedValue({
      executionId: 'execution',
      actionCode: 'confirm',
      document: emptyState.document,
      documentVersion: 8,
      actions: state.actions,
      workCenterMayChange: false,
      createdDocument: null,
    })
    const values = createArgs(state)
    const actions = useConfiguredEntityEditorDocumentActions(values.args)
    await flushAsyncWork()

    expect(actions.requestDocumentAction('confirm')).toBe(true)
    expect(actions.confirmation.value).toMatchObject({
      actionCode: 'confirm',
      title: 'Confirm action?',
      message: 'Continue?',
      confirmLabel: 'Confirm',
      requireReason: false,
    })
    actions.cancelDocumentActionConfirmation()
    expect(actions.confirmation.value).toBeNull()
    expect(executeDocumentActionMock).not.toHaveBeenCalled()
    await actions.confirmDocumentAction()
    expect(executeDocumentActionMock).not.toHaveBeenCalled()

    actions.requestDocumentAction('confirm')
    await actions.confirmDocumentAction()
    actions.requestDocumentAction('reason')
    expect(actions.confirmation.value?.requireReason).toBe(true)
    await actions.confirmDocumentAction('   ')
    expect(actions.confirmation.value?.actionCode).toBe('reason')
    expect(executeDocumentActionMock).toHaveBeenCalledTimes(1)
    await actions.confirmDocumentAction('  approved  ')

    expect(executeDocumentActionMock).toHaveBeenCalledTimes(2)
    expect(executeDocumentActionMock).toHaveBeenNthCalledWith(
      2,
      'pm.invoice',
      'doc-1',
      'reason',
      { expectedVersion: 8, reason: 'approved' },
    )
  })

  it('keeps a pending confirmation while its command is executing', async () => {
    const action = {
      code: 'confirm',
      label: 'Confirm',
      icon: 'check',
      kind: 'Primary' as const,
      executionKind: 'Command' as const,
      order: 1,
      isAllowed: true,
      disabledReasons: [],
      confirmation: {
        mode: 'Confirm' as const,
        title: 'Confirm?',
        message: 'Continue?',
        confirmLabel: 'Confirm',
      },
    }
    const state = { ...emptyState, actions: [action] } satisfies DocumentEditorStateDto
    let resolveExecution!: (value: unknown) => void
    executeDocumentActionMock.mockReturnValueOnce(new Promise((resolve) => { resolveExecution = resolve }))
    const values = createArgs(state)
    const actions = useConfiguredEntityEditorDocumentActions(values.args)
    await flushAsyncWork()

    actions.requestDocumentAction('confirm')
    const confirmation = actions.confirmDocumentAction()
    await flushAsyncWork()
    expect(actions.executingDocumentAction.value).toBe(true)
    actions.cancelDocumentActionConfirmation()
    expect(actions.confirmation.value?.actionCode).toBe('confirm')

    resolveExecution({
      executionId: 'execution',
      actionCode: 'confirm',
      document: emptyState.document,
      documentVersion: 8,
      actions: [],
      workCenterMayChange: false,
      createdDocument: null,
    })
    await confirmation
    expect(actions.confirmation.value).toBeNull()
  })

  it('disables all actions while busy and reloads when no document applicator is supplied', async () => {
    let resolveExecution!: (result: {
      executionId: string
      actionCode: string
      document: typeof emptyState.document
      documentVersion: number
      actions: DocumentEditorStateDto['actions']
      workCenterMayChange: boolean
      createdDocument: null
    }) => void
    executeDocumentActionMock.mockReturnValueOnce(new Promise((resolve) => {
      resolveExecution = resolve
    }))
    const state = {
      ...emptyState,
      actions: [{
        code: 'approve',
        label: 'Approve',
        icon: 'check',
        kind: 'Primary',
        executionKind: 'Command',
        order: 1,
        isAllowed: true,
        disabledReasons: [],
      }, {
        code: 'secondary',
        label: 'Secondary',
        icon: 'play',
        kind: 'Secondary',
        executionKind: 'Command',
        order: 2,
        isAllowed: true,
        disabledReasons: [],
      }],
    } satisfies DocumentEditorStateDto
    const values = createArgs(state)
    delete (values.args as Partial<typeof values.args>).applyActionDocument
    const actions = useConfiguredEntityEditorDocumentActions(values.args)
    await flushAsyncWork()

    values.loading.value = true
    expect(actions.extraPrimaryActions.value[0]?.disabled).toBe(true)
    values.loading.value = false
    values.saving.value = true
    expect(actions.extraPrimaryActions.value[0]?.disabled).toBe(true)
    values.saving.value = false

    actions.handleConfiguredAction('document-action:approve')
    await flushAsyncWork()
    expect(actions.executingDocumentAction.value).toBe(true)
    expect(actions.extraMoreActionGroups.value[0]?.items[0]?.disabled).toBe(true)
    expect(actions.handleConfiguredAction('document-action:secondary')).toBe(true)
    expect(executeDocumentActionMock).toHaveBeenCalledTimes(1)

    resolveExecution({
      executionId: 'execution',
      actionCode: 'approve',
      document: emptyState.document,
      documentVersion: 8,
      actions: [],
      workCenterMayChange: false,
      createdDocument: null,
    })
    await flushAsyncWork()
    expect(values.reloadDocument).toHaveBeenCalledOnce()
  })

  it('leaves lifecycle actions to the fixed editor toolbar and hides repost duplicates', async () => {
    const state = {
      ...emptyState,
      actions: [
        'post',
        'unpost',
        'repost',
        'mark_for_deletion',
        'unmark_for_deletion',
      ].map((code, order) => ({
        code,
        label: code,
        icon: 'play',
        kind: 'Primary' as const,
        executionKind: 'Command' as const,
        order,
        isAllowed: true,
        disabledReasons: [],
      })),
    } satisfies DocumentEditorStateDto
    const values = createArgs(state)
    const actions = useConfiguredEntityEditorDocumentActions(values.args)
    await flushAsyncWork()

    expect(actions.hasUnifiedActionState.value).toBe(true)
    expect(actions.documentLifecycleActions.value).toEqual({
      deletion: {
        key: 'document-action:unmark_for_deletion',
        title: 'Unmark for deletion',
        icon: 'trash-restore',
        disabled: false,
      },
      posting: {
        key: 'document-action:unpost',
        title: 'Unpost',
        icon: 'undo',
        disabled: false,
      },
    })
    expect(actions.extraPrimaryActions.value).toEqual([])
    expect(actions.extraMoreActionGroups.value).toEqual([])
    expect(actions.handleConfiguredAction('document-action:post')).toBe(true)
    expect(actions.handleConfiguredAction('document-action:repost')).toBe(false)
  })

  it('does nothing when execution loses its document state or a derivation has no route', async () => {
    const state = {
      ...emptyState,
      actions: [{
        code: 'derive',
        label: 'Derive',
        icon: 'file-text',
        kind: 'Secondary',
        executionKind: 'Derivation',
        order: 1,
        isAllowed: true,
        disabledReasons: [],
      }],
    } satisfies DocumentEditorStateDto
    executeDocumentActionMock.mockResolvedValueOnce({
      executionId: 'execution',
      actionCode: 'derive',
      document: emptyState.document,
      documentVersion: 8,
      actions: state.actions,
      workCenterMayChange: false,
      createdDocument: { ...emptyState.document, id: 'created-1' },
    })
    const values = createArgs(state)
    const actions = useConfiguredEntityEditorDocumentActions(values.args)
    await flushAsyncWork()
    actions.handleConfiguredAction('document-action:derive')
    await flushAsyncWork()
    expect(values.requestNavigate).not.toHaveBeenCalled()

    values.currentId.value = null
    expect(actions.handleConfiguredAction('document-action:derive')).toBe(true)
    await flushAsyncWork()
    expect(executeDocumentActionMock).toHaveBeenCalledTimes(1)
  })
})
