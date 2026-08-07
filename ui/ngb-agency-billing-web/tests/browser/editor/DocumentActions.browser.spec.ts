import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'

const mocks = vi.hoisted(() => ({
  configuredArgs: null as Record<string, unknown> | null,
  headerArgs: null as Record<string, unknown> | null,
  paletteArgs: null as Record<string, unknown> | null,
  handleConfiguredAction: vi.fn(),
  requestDocumentAction: vi.fn(),
  isDocumentActionAllowed: vi.fn(),
  refreshDocumentActions: vi.fn(),
  routerReplace: vi.fn(),
  navigateBack: vi.fn(),
  runEntityEditorAction: vi.fn(),
  persistenceArgs: null as Record<string, unknown> | null,
  navigationArgs: null as Record<string, unknown> | null,
  lifecycleArgs: null as Record<string, unknown> | null,
  leaveArgs: null as Record<string, unknown> | null,
  pageArgs: null as Record<string, unknown> | null,
  outputsArgs: null as Record<string, unknown> | null,
  handleDocumentHeaderAction: vi.fn(),
  canMarkForDeletion: null as { value: boolean } | null,
  canUnmarkForDeletion: null as { value: boolean } | null,
  loadDocumentEffectsSnapshot: vi.fn(),
  save: vi.fn(),
  unmarkForDeletion: vi.fn(),
  requestMarkForDeletion: vi.fn(),
  focusField: vi.fn(),
  focusFirstError: vi.fn(),
  adapterContext: null as Record<string, unknown> | null,
  hasTag: vi.fn(() => false),
  pmErrorArgs: null as Record<string, unknown> | null,
  pmErrorState: null as Record<string, any> | null,
  pmLeaseState: null as Record<string, any> | null,
  pmCanBulkCreateUnits: null as { value: boolean } | null,
}))

vi.mock('vue-router', async (importOriginal) => ({
  ...(await importOriginal<typeof import('vue-router')>()),
  useRoute: () => ({ name: 'document' }),
  useRouter: () => ({
    push: vi.fn(),
    replace: mocks.routerReplace,
  }),
}))

vi.mock('../../../src/editor/AgencyBillingDocumentPartsEditor.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return {
    default: defineComponent(() => () => h('div')),
  }
})

vi.mock('../../../src/editor/useCatalogEntityEditorPersistence', () => ({
  useCatalogEntityEditorPersistence: vi.fn((context: Record<string, unknown>) => {
    mocks.adapterContext = context
    return {}
  }),
}))

vi.mock('../../../src/editor/useDocumentEntityEditorPersistence', () => ({
  useDocumentEntityEditorPersistence: vi.fn((context: Record<string, unknown>) => {
    mocks.adapterContext = context
    return {}
  }),
}))

vi.mock('../../../src/metadata/framework', () => ({
  agencyBillingMetadataFormBehavior: {},
}))

vi.mock('../../../../ngb-trade-web/src/editor/TradeDocumentPartsEditor.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return {
    default: defineComponent(() => () => h('div')),
  }
})

vi.mock('../../../../ngb-trade-web/src/editor/useCatalogEntityEditorPersistence', () => ({
  useCatalogEntityEditorPersistence: vi.fn((context: Record<string, unknown>) => {
    mocks.adapterContext = context
    return {}
  }),
}))

vi.mock('../../../../ngb-trade-web/src/editor/useDocumentEntityEditorPersistence', () => ({
  useDocumentEntityEditorPersistence: vi.fn((context: Record<string, unknown>) => {
    mocks.adapterContext = context
    return {}
  }),
}))

vi.mock('../../../../ngb-trade-web/src/metadata/framework', () => ({
  tradeMetadataFormBehavior: {},
}))

vi.mock('../../../../ngb-crm-web/src/editor/CRMDocumentPartsEditor.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent(() => () => h('div')) }
})

vi.mock('../../../../ngb-crm-web/src/editor/useCatalogEntityEditorPersistence', () => ({
  useCatalogEntityEditorPersistence: vi.fn((context: Record<string, unknown>) => {
    mocks.adapterContext = context
    return {}
  }),
}))

vi.mock('../../../../ngb-crm-web/src/editor/useDocumentEntityEditorPersistence', () => ({
  useDocumentEntityEditorPersistence: vi.fn((context: Record<string, unknown>) => {
    mocks.adapterContext = context
    return {}
  }),
}))

vi.mock('../../../../ngb-crm-web/src/metadata/framework', () => ({
  crmMetadataFormBehavior: {},
}))

vi.mock('../../../../ngb-property-management-web/src/components/lease/LeaseTenantsGrid.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent(() => () => h('div')) }
})

vi.mock('../../../../ngb-property-management-web/src/components/property/PmPropertyBulkCreateUnitsDialog.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent(() => () => h('div')) }
})

vi.mock('../../../../ngb-property-management-web/src/editor/entityProfile', () => ({
  PM_EDITOR_TAGS: {
    PROPERTY_CATALOG: 'property',
    LEASE_DOCUMENT: 'lease',
  },
}))

vi.mock('../../../../ngb-property-management-web/src/editor/pm/useCatalogEntityEditorPersistence', () => ({
  useCatalogEntityEditorPersistence: vi.fn((context: Record<string, unknown>) => {
    mocks.adapterContext = context
    return {}
  }),
}))

vi.mock('../../../../ngb-property-management-web/src/editor/pm/useDocumentEntityEditorPersistence', () => ({
  useDocumentEntityEditorPersistence: vi.fn((context: Record<string, unknown>) => {
    mocks.adapterContext = context
    return {}
  }),
}))

vi.mock('../../../../ngb-property-management-web/src/editor/pm/useEntityEditorErrorState', async () => {
  const { ref } = await import('vue')
  return {
    useEntityEditorErrorState: (args: Record<string, unknown>) => {
      mocks.pmErrorArgs = args
      const state = {
        error: ref(null),
        displayedError: ref(null),
        inlineFieldErrors: ref({}),
        leaseTenantValidation: ref({}),
        bannerIssues: ref([]),
        normalizeEditorError: vi.fn(() => ({ summary: 'error', issues: [] })),
        setEditorError: vi.fn(),
        dismissFieldIssues: vi.fn(),
        dismissLeaseIssues: vi.fn(),
      }
      mocks.pmErrorState = state
      return state
    },
  }
})

vi.mock('../../../../ngb-property-management-web/src/editor/pm/useEntityEditorLeasePart', async () => {
  const { ref } = await import('vue')
  return {
    useEntityEditorLeasePart: () => {
      const state = {
        leasePartiesRows: ref([]),
        buildCopyParts: vi.fn(() => null),
        applyInitialParts: vi.fn(),
        applyPersistedParts: vi.fn(),
        buildSaveParts: vi.fn(() => null),
        ensureLeasePartiesInitialized: vi.fn(),
        validateLeasePartiesBeforeSave: vi.fn(() => null),
        isLeaseDocument: ref(false),
      }
      mocks.pmLeaseState = state
      return state
    },
  }
})

vi.mock('../../../../ngb-property-management-web/src/editor/pm/usePmCatalogEntityEditorCapabilities', async () => {
  const { ref } = await import('vue')
  return {
    usePmCatalogEntityEditorCapabilities: () => {
      const canBulkCreateUnits = ref(false)
      mocks.pmCanBulkCreateUnits = canBulkCreateUnits
      return { canBulkCreateUnits }
    },
  }
})

vi.mock('@ngbplatform/ui', async () => {
  const { computed, defineComponent, h, ref } = await import('vue')
  const yes = ref(true)
  const no = ref(false)
  const canMarkForDeletion = ref(true)
  const canUnmarkForDeletion = ref(false)
  const empty = ref('')
  const noop = vi.fn()
  mocks.canMarkForDeletion = canMarkForDeletion
  mocks.canUnmarkForDeletion = canUnmarkForDeletion

  return {
    NgbEntityEditor: defineComponent({
      props: {
        saving: Boolean,
      },
      emits: [
        'back', 'close', 'action', 'closeAuditLog', 'cancelLeave', 'confirmLeave',
        'cancelMarkForDeletion', 'confirmMarkForDeletion', 'cancelUnpost', 'confirmUnpost',
      ],
      setup(props, { emit, expose }) {
        mocks.focusField.mockImplementation((path: string) => path === 'known')
        mocks.focusFirstError.mockReturnValue(true)
        expose({ focusField: mocks.focusField, focusFirstError: mocks.focusFirstError })
        const events = [
          'back', 'close', 'closeAuditLog', 'cancelLeave', 'confirmLeave',
          'cancelMarkForDeletion', 'confirmMarkForDeletion', 'cancelUnpost', 'confirmUnpost',
        ]
        return () => h('div', {
          'data-testid': 'editor-shell',
          'data-saving': String(props.saving),
        }, [
          ...events.map((event) => h('button', { 'data-editor-event': event, onClick: () => emit(event) }, event)),
          h('button', { 'data-editor-event': 'action', onClick: () => emit('action', 'save') }, 'action'),
        ])
      },
    }),
    clonePlainData: (value: unknown) => value == null ? value : JSON.parse(JSON.stringify(value)),
    navigateBack: mocks.navigateBack,
    normalizeDocumentStatusValue: (value: unknown) => Number(value) || 1,
    normalizeEntityEditorError: (cause: unknown, options: { resolveIssueLabel: (path: string) => string }) => {
      options.resolveIssueLabel('form.base')
      options.resolveIssueLabel('known')
      options.resolveIssueLabel('unknown_key')
      return { summary: cause instanceof Error ? cause.message : 'error', issues: [] }
    },
    runEntityEditorAction: mocks.runEntityEditorAction,
    stableStringify: (value: unknown) => JSON.stringify(value),
    humanizeEntityEditorFieldKey: (value: string) => value,
    isEntityEditorFormIssuePath: (path: string) => path.startsWith('form.'),
    useMetadataStore: () => ({}),
    useLookupStore: () => ({}),
    useToasts: () => ({ push: noop }),
    useEntityEditorBusinessContext: () => ({
      currentEditorContext: () => ({ kind: 'document', typeCode: 'ab.invoice' }),
      hasTag: mocks.hasTag,
    }),
    useEntityEditorCapabilities: () => ({
      canOpenAudit: yes,
      canShareLink: yes,
      canOpenEffectsPage: yes,
      canOpenDocumentFlowPage: yes,
      canPrintDocument: yes,
      canMarkForDeletion,
      canUnmarkForDeletion,
      canDelete: yes,
      canSave: yes,
      documentStatusLabel: empty,
      documentStatusTone: empty,
      title: ref('Invoice'),
      subtitle: empty,
      auditEntityKind: empty,
      auditEntityId: empty,
      auditEntityTitle: empty,
      isReadOnly: no,
    }),
    useEntityEditorLeaveGuard: (args: Record<string, unknown>) => {
      mocks.leaveArgs = args
      return {
      leaveOpen: no,
      requestNavigate: noop,
      requestClose: noop,
      confirmLeave: noop,
      cancelLeave: noop,
      }
    },
    useEntityEditorPersistence: (args: Record<string, unknown>) => {
      mocks.persistenceArgs = args
      return {
      load: vi.fn().mockResolvedValue(undefined),
      save: mocks.save,
      markForDeletion: vi.fn().mockResolvedValue(undefined),
      unmarkForDeletion: mocks.unmarkForDeletion,
      deleteEntity: vi.fn().mockResolvedValue(undefined),
      loadDocumentEffectsSnapshot: mocks.loadDocumentEffectsSnapshot,
      }
    },
    useEntityEditorNavigationActions: (args: Record<string, unknown>) => {
      mocks.navigationArgs = args
      return {
      auditOpen: no,
      fallbackCloseTarget: '/',
      copyShareLink: vi.fn().mockResolvedValue(undefined),
      copyDocument: noop,
      openDocumentPrintPage: noop,
      openAuditLog: noop,
      closeAuditLog: noop,
      openDocumentEffectsPage: noop,
      openDocumentFlowPage: noop,
      openFullPage: noop,
      openCompactPage: noop,
      closePage: noop,
      }
    },
    useConfiguredEntityEditorDocumentActions: (args: Record<string, unknown>) => {
      mocks.configuredArgs = args
      const apply = args.applyActionDocument as (document: unknown) => void
      apply({
        id: 'doc-1',
        status: 2,
        isMarkedForDeletion: false,
        payload: { fields: { memo: 'updated' } },
      })
      apply({
        id: 'doc-1',
        status: 2,
        isMarkedForDeletion: false,
      })
      return {
        documentLifecycleActions: ref({ deletion: null, posting: null }),
        extraPrimaryActions: ref([]),
        extraMoreActionGroups: ref([]),
        handleConfiguredAction: mocks.handleConfiguredAction,
        requestDocumentAction: mocks.requestDocumentAction,
        isDocumentActionAllowed: mocks.isDocumentActionAllowed,
        confirmation: ref(null),
        cancelDocumentActionConfirmation: noop,
        confirmDocumentAction: noop,
        hasUnifiedActionState: yes,
        executingDocumentAction: yes,
        refreshDocumentActions: mocks.refreshDocumentActions,
      }
    },
    useEntityEditorHeaderActions: (args: Record<string, unknown>) => {
      mocks.headerArgs = args
      void (args.saving as { value: boolean }).value
      ;(args.onUnhandledAction as (action: string) => void)('document-action:post')
      return {
        documentPrimaryActions: ref([]),
        documentMoreActionGroups: ref([]),
        handleDocumentHeaderAction: mocks.handleDocumentHeaderAction,
      }
    },
    useEntityEditorLifecycleConfirmations: (args: Record<string, unknown>) => {
      mocks.lifecycleArgs = args
      return {
      markConfirmOpen: no,
      markConfirmMessage: empty,
      requestMarkForDeletion: mocks.requestMarkForDeletion,
      cancelMarkForDeletion: noop,
      confirmMarkForDeletion: noop,
      }
    },
    useEntityEditorCommandPalette: (args: Record<string, unknown>) => {
      mocks.paletteArgs = args
      ;(args.isDocumentActionAllowed as (actionCode: string) => boolean)('post')
    },
    useEntityEditorPageActions: (args: Record<string, unknown>) => {
      mocks.pageArgs = args
      return ref([])
    },
    useEntityEditorOutputs: (args: Record<string, unknown>) => {
      mocks.outputsArgs = args
      return { flags: computed(() => ({})) }
    },
  }
})

import AgencyBillingEntityEditor from '../../../src/editor/AgencyBillingEntityEditor.vue'
import PmEntityEditor from '../../../../ngb-property-management-web/src/editor/pm/PmEntityEditor.vue'
import TradeEntityEditor from '../../../../ngb-trade-web/src/editor/TradeEntityEditor.vue'
import CRMEntityEditor from '../../../../ngb-crm-web/src/editor/CRMEntityEditor.vue'

test.each([
  ['agency billing', AgencyBillingEntityEditor, 'ab.invoice'],
  ['property management', PmEntityEditor, 'pm.receivable_payment'],
  ['trade', TradeEntityEditor, 'trd.sales_invoice'],
])('connects unified document actions to the %s editor shell', async (_, component, typeCode) => {
  mocks.handleConfiguredAction.mockClear()
  mocks.refreshDocumentActions.mockReset().mockResolvedValue(undefined)
  mocks.requestDocumentAction.mockReset().mockReturnValue(true)
  mocks.isDocumentActionAllowed.mockReset().mockReturnValue(true)
  const view = await render(component, {
    props: {
      kind: 'document',
      typeCode,
      id: 'doc-1',
    },
  })

  await expect.element(view.getByTestId('editor-shell')).toHaveAttribute('data-saving', 'true')
  expect(mocks.configuredArgs).toMatchObject({
    applyActionDocument: expect.any(Function),
  })
  expect(mocks.headerArgs).toMatchObject({
    documentLifecycleActions: expect.objectContaining({ value: { deletion: null, posting: null } }),
    onUnhandledAction: expect.any(Function),
  })
  expect(mocks.headerArgs).not.toHaveProperty('suppressBuiltInDocumentLifecycleActions')
  expect(mocks.handleConfiguredAction).toHaveBeenCalledWith('document-action:post')
  expect(mocks.paletteArgs).toMatchObject({
    isDocumentActionAllowed: mocks.isDocumentActionAllowed,
    requestDocumentAction: mocks.requestDocumentAction,
  })
  expect(mocks.isDocumentActionAllowed).toHaveBeenCalledWith('post')

  const applyDocument = mocks.configuredArgs?.applyActionDocument as (document: unknown) => void
  applyDocument({ id: 'doc-1', status: 3, payload: { fields: {} } })
  await vi.waitFor(() => expect(mocks.refreshDocumentActions).toHaveBeenCalledOnce())
  mocks.refreshDocumentActions.mockRejectedValueOnce(new Error('refresh failed'))
  applyDocument({ id: 'doc-1', status: 4, payload: { fields: {} } })
  await vi.waitFor(() => expect(mocks.refreshDocumentActions).toHaveBeenCalledTimes(2))
  view.unmount()

  const catalogView = await render(component, {
    props: { kind: 'catalog', typeCode, id: 'catalog-1' },
  })
  ;(mocks.configuredArgs?.applyActionDocument as (document: unknown) => void)({
    id: 'catalog-1', status: 3, payload: { fields: {} },
  })
  await nextTick()
  expect(mocks.refreshDocumentActions).toHaveBeenCalledTimes(2)
  catalogView.unmount()

  const newDocumentView = await render(component, {
    props: { kind: 'document', typeCode },
  })
  ;(mocks.configuredArgs?.applyActionDocument as (document: unknown) => void)({
    id: 'draft', status: 3, payload: { fields: {} },
  })
  await nextTick()
  expect(mocks.refreshDocumentActions).toHaveBeenCalledTimes(2)
  newDocumentView.unmount()
})

function editorState(wrapper: ReturnType<typeof mount>): Record<string, any> {
  return (wrapper.vm as any).$?.setupState
}

test.each([
  ['agency billing', AgencyBillingEntityEditor, 'ab.invoice'],
  ['trade', TradeEntityEditor, 'trd.sales_invoice'],
  ['CRM', CRMEntityEditor, 'crm.quote'],
])('covers the complete %s entity-editor shell orchestration', async (_, component, typeCode) => {
  let allowedDocumentActions = new Set(['post', 'unpost', 'mark_for_deletion', 'unmark_for_deletion'])
  mocks.isDocumentActionAllowed.mockReset().mockImplementation(
    (actionCode: string) => allowedDocumentActions.has(actionCode),
  )
  mocks.requestDocumentAction.mockReset().mockImplementation(
    (actionCode: string) => allowedDocumentActions.has(actionCode),
  )
  mocks.canMarkForDeletion!.value = true
  mocks.canUnmarkForDeletion!.value = false
  mocks.unmarkForDeletion.mockReset().mockResolvedValue(undefined)
  mocks.requestMarkForDeletion.mockReset()
  mocks.loadDocumentEffectsSnapshot.mockReset().mockResolvedValue(undefined)
  mocks.save.mockReset().mockResolvedValue(undefined)
  mocks.navigateBack.mockClear()
  mocks.runEntityEditorAction.mockClear()
  mocks.handleDocumentHeaderAction.mockClear()
  mocks.focusField.mockClear()
  mocks.focusFirstError.mockClear()

  const wrapper = mount(component, {
    attachTo: document.body,
    props: {
      kind: 'document', typeCode, id: 'doc-1', closeTo: '/close', expandTo: '/full', compactTo: '/compact',
      initialFields: { initial: true }, initialParts: { initial: { rows: [] } },
    },
  })
  await nextTick()
  const state = editorState(wrapper)

  expect(state.editorKind).toBe('document')
  expect(state.editorTypeCode).toBe(typeCode)
  expect(state.editorMode).toBe('page')
  expect(state.editorInitialFields).toEqual({ initial: true })
  expect(state.editorInitialParts).toEqual({ initial: { rows: [] } })
  expect(state.editorExpandTo).toBe('/full')
  expect(state.editorCompactTo).toBe('/compact')
  expect(state.editorCloseTo).toBe('/close')
  expect(state.editorNavigateOnCreate).toBeUndefined()
  expect(state.currentIdValue).toBe('doc-1')
  expect(state.isDraft).toBe(false)
  expect(state.isDeletedStatus).toBe(false)
  expect(state.fieldLabels).toEqual({})

  await wrapper.setProps({ id: 'doc-2' })
  expect(state.currentId).toBe('doc-2')
  await wrapper.setProps({ id: null })
  expect(state.currentId).toBeNull()
  await wrapper.setProps({ id: 'doc-1' })

  state.docMeta = {
    form: {
      sections: [
        {
          rows: [
            {
              fields: [
                null,
                { key: 'known', label: 'Known field' },
                { key: 'known_empty', label: 'Known empty' },
                { key: 'blank', label: ' ' },
                { key: '', label: 'Ignored' },
              ],
            },
            { fields: null },
          ],
        },
        { rows: null },
      ],
    },
    parts: [{ partCode: 'lines', title: 'Lines', list: { columns: [] } }],
  }
  await nextTick()
  expect(state.fieldLabels).toEqual({ known: 'Known field', known_empty: 'Known empty' })
  expect(state.resolveIssueLabel(null as never)).toBe('')
  expect(state.resolveIssueLabel('form.base')).toBe('Validation')
  expect(state.resolveIssueLabel('known')).toBe('Known field')
  expect(state.resolveIssueLabel('unknown_key')).toBe('unknown_key')
  expect(state.normalizeEditorError(new Error('boom')).summary).toBe('boom')

  const veryLargeRowIndex = '9'.repeat(400)
  state.error = {
    summary: 'Invalid',
    issues: [
      { path: 'form.base', scope: 'form', messages: ['Form error'] },
      { path: 'known', scope: 'field', messages: ['', 'Known error'] },
      { path: 'known', scope: 'field', messages: ['Duplicate'] },
      { path: 'known_empty', scope: 'field', messages: [null, ' '] },
      { path: 'unknown', scope: 'field', messages: ['Unknown'] },
      { path: 'blank', scope: 'form', messages: ['Wrong scope'] },
      { path: 'parts.lines.rows[0].memo', scope: 'field', messages: ['', 'Part error'] },
      { path: 'parts.lines.rows[0].memo', scope: 'field', messages: ['Duplicate part'] },
      { path: 'parts.lines.rows[1].memo', scope: 'field', messages: [] },
      { path: `parts.lines.rows[${veryLargeRowIndex}].memo`, scope: 'field', messages: ['Overflow'] },
      { path: 'parts.invalid', scope: 'field', messages: ['No match'] },
    ],
  }
  await nextTick()
  expect(state.inlineFieldErrors).toEqual({ known: 'Known error' })
  expect(state.partErrors.lines[0].memo).toBe('Part error')
  expect(state.bannerIssues).toHaveLength(11)

  mocks.focusField.mockImplementation((path: string) => path === 'known')
  state.focusFirstValidationError(state.error)
  expect(mocks.focusField).toHaveBeenCalledWith('known')
  mocks.focusField.mockReturnValue(false)
  state.focusFirstValidationError(state.error)
  expect(mocks.focusFirstError).toHaveBeenCalledWith(['known'])
  state.focusFirstValidationError(null)
  state.setEditorError(null)
  state.setEditorError({ summary: 'Delayed', issues: [{ path: 'known', scope: 'field', messages: ['x'] }] })
  await nextTick()

  state.model = { known: 'value' }
  state.partsModel = { lines: { rows: [] } }
  state.resetInitialSnapshot()
  expect(state.isDirty).toBe(false)
  state.model = { known: 'changed' }
  await nextTick()
  expect(state.isDirty).toBe(true)

  const beforeExecute = mocks.configuredArgs?.beforeExecute as (actionCode: string) => Promise<unknown>
  expect(await beforeExecute('view_flow')).toBe(true)
  state.error = null
  mocks.save.mockImplementationOnce(async () => { state.resetInitialSnapshot() })
  expect(await beforeExecute('post')).toEqual({ proceed: true, refreshState: true })
  state.model = { known: 'dirty-with-error' }
  await nextTick()
  mocks.save.mockImplementationOnce(async () => {
    state.error = { summary: 'Save failed', issues: [] }
    state.resetInitialSnapshot()
  })
  expect(await beforeExecute('post')).toEqual({ proceed: false, refreshState: true })
  state.error = null
  state.model = { known: 'still-dirty' }
  await nextTick()
  mocks.save.mockResolvedValueOnce(undefined)
  expect(await beforeExecute('post')).toEqual({ proceed: false, refreshState: true })

  allowedDocumentActions = new Set(['post', 'unpost', 'mark_for_deletion', 'unmark_for_deletion'])
  expect(state.canPost).toBe(true)
  expect(state.canUnpost).toBe(true)
  await (wrapper.vm as any).markForDeletion()
  await (wrapper.vm as any).unmarkForDeletion()
  await (wrapper.vm as any).post()
  await (wrapper.vm as any).unpost()
  expect(mocks.requestDocumentAction).toHaveBeenCalledWith('mark_for_deletion')
  expect(mocks.requestDocumentAction).toHaveBeenCalledWith('unmark_for_deletion')
  expect(mocks.requestDocumentAction).toHaveBeenCalledWith('post')
  expect(mocks.requestDocumentAction).toHaveBeenCalledWith('unpost')

  allowedDocumentActions = new Set(['unmark_for_deletion'])
  state.toggleMarkForDeletion()
  expect(mocks.requestDocumentAction).toHaveBeenLastCalledWith('unmark_for_deletion')
  allowedDocumentActions = new Set(['mark_for_deletion'])
  state.toggleMarkForDeletion()
  expect(mocks.requestDocumentAction).toHaveBeenLastCalledWith('mark_for_deletion')
  allowedDocumentActions.clear()
  state.toggleMarkForDeletion()
  expect(mocks.requestDocumentAction).toHaveBeenLastCalledWith('mark_for_deletion')

  allowedDocumentActions = new Set(['unpost'])
  state.togglePost()
  expect(mocks.requestDocumentAction).toHaveBeenLastCalledWith('unpost')
  allowedDocumentActions = new Set(['post'])
  state.togglePost()
  expect(mocks.requestDocumentAction).toHaveBeenLastCalledWith('post')
  allowedDocumentActions.clear()
  state.togglePost()
  expect(mocks.requestDocumentAction).toHaveBeenLastCalledWith('post')

  mocks.refreshDocumentActions.mockRejectedValueOnce(new Error('refresh failed'))
  ;(mocks.configuredArgs?.applyActionDocument as (document: unknown) => void)({ status: 4, payload: { fields: {} } })
  await vi.waitFor(() => expect(mocks.refreshDocumentActions).toHaveBeenCalled())

  state.handleHeaderAction('save')
  expect(mocks.handleDocumentHeaderAction).toHaveBeenCalledWith('save')
  expect(state.afterFormExtensions).toHaveLength(1)
  state.afterFormExtensions[0].props['onUpdate:modelValue']({ lines: { rows: [{ memo: 'updated' }] } })
  expect(state.partsModel.lines.rows[0].memo).toBe('updated')
  state.loading = true
  await nextTick()
  expect(state.afterFormExtensions).toEqual([])
  state.loading = false

  expect((mocks.navigationArgs?.buildCopyParts as () => unknown)()).toEqual(state.partsModel)
  state.partsModel = null
  expect((mocks.navigationArgs?.buildCopyParts as () => unknown)()).toBeNull()
  expect((mocks.configuredArgs?.loading as { value: boolean }).value).toBe(false)
  expect((mocks.configuredArgs?.saving as { value: boolean }).value).toBe(false)
  expect((mocks.headerArgs?.loading as { value: boolean }).value).toBe(false)
  expect((mocks.headerArgs?.saving as { value: boolean }).value).toBe(true)
  expect((mocks.pageArgs?.loading as { value: boolean }).value).toBe(false)
  expect((mocks.pageArgs?.saving as { value: boolean }).value).toBe(false)
  expect((mocks.outputsArgs?.loading as { value: boolean }).value).toBe(false)
  expect((mocks.outputsArgs?.saving as { value: boolean }).value).toBe(false)
  expect((mocks.outputsArgs?.canExpand as { value: boolean }).value).toBe(true)

  ;(mocks.leaveArgs?.onClose as () => void)()
  ;(mocks.adapterContext?.emitCreated as (id: string) => void)('created-1')
  ;(mocks.adapterContext?.emitSaved as () => void)()
  ;(mocks.adapterContext?.emitChanged as (reason: string) => void)('field')
  ;(mocks.adapterContext?.emitDeleted as () => void)()
  ;(mocks.persistenceArgs?.emitChanged as (reason: string) => void)('parts')
  ;(mocks.persistenceArgs?.emitDeleted as () => void)()
  await state.pageActionHandlers.copyShareLink()
  await state.pageActionHandlers.save()
  ;(mocks.configuredArgs?.applyActionDocument as (document: unknown) => void)({ payload: { fields: { applied: true } } })
  expect(state.model).toEqual({ applied: true })

  state.docEffects = { accountingEntries: [] }
  expect((wrapper.vm as any).getDocumentEffects()).toEqual({ accountingEntries: [] })
  state.currentId = null
  expect(await (wrapper.vm as any).reloadDocumentEffects()).toBeNull()
  state.currentId = 'doc-1'
  expect(await (wrapper.vm as any).reloadDocumentEffects()).toEqual({ accountingEntries: [] })
  expect(mocks.loadDocumentEffectsSnapshot).toHaveBeenCalledWith(typeCode, 'doc-1')
  state.model = { applied: false }
  await nextTick()
  expect((wrapper.vm as any).getIsDirty()).toBe(true)
  expect((wrapper.vm as any).getCanSave()).toBe(true)
  expect((wrapper.vm as any).getFlags()).toEqual({})

  for (const button of wrapper.findAll('[data-editor-event]')) await button.trigger('click')
  expect(mocks.navigateBack).toHaveBeenCalled()
  wrapper.unmount()

  const catalog = mount(component, {
    props: { kind: 'catalog', typeCode, id: 'catalog-1' },
  })
  await nextTick()
  const catalogState = editorState(catalog)
  expect(catalogState.editorMode).toBe('page')
  expect(catalogState.editorInitialFields).toBeNull()
  expect(catalogState.editorInitialParts).toBeNull()
  expect(catalogState.editorExpandTo).toBeNull()
  expect(catalogState.editorCompactTo).toBeNull()
  expect(catalogState.editorCloseTo).toBeNull()
  expect(catalogState.editorNavigateOnCreate).toBeUndefined()
  expect((mocks.outputsArgs?.canExpand as { value: boolean }).value).toBe(false)
  catalogState.catalogMeta = { form: null }
  catalogState.catalogItem = { isMarkedForDeletion: true, isDeleted: false }
  expect(catalogState.isMarkedForDeletion).toBe(true)
  catalogState.catalogItem = { isMarkedForDeletion: false, isDeleted: true }
  expect(catalogState.isMarkedForDeletion).toBe(true)
  catalogState.catalogItem = null
  expect(catalogState.isMarkedForDeletion).toBe(false)
  await (catalog.vm as any).markForDeletion()
  await (catalog.vm as any).unmarkForDeletion()
  mocks.canUnmarkForDeletion!.value = true
  catalogState.toggleMarkForDeletion()
  expect(mocks.unmarkForDeletion).toHaveBeenCalledTimes(2)
  mocks.canUnmarkForDeletion!.value = false
  mocks.canMarkForDeletion!.value = true
  catalogState.toggleMarkForDeletion()
  expect(mocks.requestMarkForDeletion).toHaveBeenCalledOnce()
  mocks.canMarkForDeletion!.value = false
  catalogState.toggleMarkForDeletion()
  expect(mocks.requestMarkForDeletion).toHaveBeenCalledOnce()
  catalogState.handleHeaderAction('save')
  expect(mocks.runEntityEditorAction).toHaveBeenCalledWith('save', expect.any(Object))
  expect(catalogState.afterFormExtensions).toEqual([])
  expect(await (catalog.vm as any).reloadDocumentEffects()).toBeNull()
  await catalog.find('[data-editor-event="back"]').trigger('click')
  catalog.unmount()

  const newDocument = mount(component, { props: { kind: 'document', typeCode } })
  expect(editorState(newDocument).currentId).toBeNull()
  newDocument.unmount()
})

test('covers the complete property-management editor orchestration and PM extensions', async () => {
  mocks.hasTag.mockReset().mockImplementation((tag: string) => tag === 'lease')
  let allowedDocumentActions = new Set(['post', 'unpost', 'mark_for_deletion', 'unmark_for_deletion'])
  mocks.isDocumentActionAllowed.mockReset().mockImplementation(
    (actionCode: string) => allowedDocumentActions.has(actionCode),
  )
  mocks.requestDocumentAction.mockReset().mockImplementation(
    (actionCode: string) => allowedDocumentActions.has(actionCode),
  )
  mocks.canMarkForDeletion!.value = true
  mocks.canUnmarkForDeletion!.value = false
  mocks.unmarkForDeletion.mockReset().mockResolvedValue(undefined)
  mocks.requestMarkForDeletion.mockReset()
  mocks.loadDocumentEffectsSnapshot.mockReset().mockResolvedValue(undefined)
  mocks.save.mockReset().mockResolvedValue(undefined)
  mocks.refreshDocumentActions.mockReset().mockResolvedValue(undefined)

  const wrapper = mount(PmEntityEditor, {
    attachTo: document.body,
    props: {
      kind: 'document', typeCode: 'pm.lease', id: 'lease-1', closeTo: '/close', expandTo: '/full', compactTo: '/compact',
      initialFields: { display: 'Lease' }, initialParts: { parties: { rows: [] } },
    },
  })
  await nextTick()
  const state = editorState(wrapper)

  expect(state.editorKind).toBe('document')
  expect(state.editorTypeCode).toBe('pm.lease')
  expect(state.editorMode).toBe('page')
  expect(state.editorInitialFields).toEqual({ display: 'Lease' })
  expect(state.editorInitialParts).toEqual({ parties: { rows: [] } })
  expect(state.editorExpandTo).toBe('/full')
  expect(state.editorCompactTo).toBe('/compact')
  expect(state.editorCloseTo).toBe('/close')
  expect(state.editorNavigateOnCreate).toBeUndefined()
  expect(state.currentIdValue).toBe('lease-1')
  state.doc = null
  expect(state.status).toBe(1)
  expect(state.isDraft).toBe(true)
  state.doc = { status: 2 }
  expect(state.isDraft).toBe(false)
  expect(state.isPmPropertyCatalog).toBe(false)
  expect(state.isLeaseDocument).toBe(true)
  expect(state.fieldLabels).toEqual({})

  await wrapper.setProps({ id: null })
  expect(state.currentId).toBeNull()
  await wrapper.setProps({ id: 'lease-1' })
  state.docMeta = {
    form: {
      sections: [
        { rows: [{ fields: [null, { key: 'display', label: 'Display' }, { key: 'blank', label: ' ' }] }, { fields: null }] },
        { rows: null },
      ],
    },
    parts: [],
  }
  await nextTick()
  expect(state.fieldLabels).toEqual({ display: 'Display' })

  state.error = { summary: 'Invalid', issues: [] }
  state.model = { display: 'A', untouched: 1 }
  await nextTick()
  state.model = { display: 'A', untouched: 1 }
  await nextTick()
  state.model = { display: 'B', added: 2 }
  await nextTick()
  expect(mocks.pmErrorState?.dismissFieldIssues).toHaveBeenCalledWith('display')
  expect(mocks.pmErrorState?.dismissFieldIssues).toHaveBeenCalledWith('added')
  state.loading = true
  state.model = { display: 'C' }
  await nextTick()
  state.loading = false
  state.saving = true
  state.model = { display: 'D' }
  await nextTick()
  state.saving = false
  state.error = null
  state.model = { display: 'E' }
  await nextTick()

  state.error = { summary: 'Lease invalid', issues: [] }
  state.leasePartiesRows = [{ party_id: 'p1' }]
  await nextTick()
  expect(mocks.pmErrorState?.dismissLeaseIssues).toHaveBeenCalled()
  state.loading = true
  state.leasePartiesRows = [{ party_id: 'p2' }]
  await nextTick()
  state.loading = false
  state.saving = true
  state.leasePartiesRows = [{ party_id: 'p3' }]
  await nextTick()
  state.saving = false
  state.error = null
  state.leasePartiesRows = [{ party_id: 'p4' }]
  await nextTick()

  state.model = { display: 'Lease' }
  state.leasePartiesRows = [{ party_id: 'p1' }]
  state.resetInitialSnapshot()
  expect(state.isDirty).toBe(false)
  state.leasePartiesRows = [{ party_id: 'p2' }]
  await nextTick()
  expect(state.isDirty).toBe(true)

  const beforeExecute = mocks.configuredArgs?.beforeExecute as (actionCode: string) => Promise<unknown>
  expect(await beforeExecute('view_flow')).toBe(true)
  state.error = null
  mocks.save.mockImplementationOnce(async () => { state.resetInitialSnapshot() })
  expect(await beforeExecute('post')).toEqual({ proceed: true, refreshState: true })
  state.model = { display: 'Save error' }
  await nextTick()
  mocks.save.mockImplementationOnce(async () => {
    state.error = { summary: 'Save failed', issues: [] }
    state.resetInitialSnapshot()
  })
  expect(await beforeExecute('post')).toEqual({ proceed: false, refreshState: true })
  state.error = null
  state.model = { display: 'Still dirty' }
  await nextTick()
  mocks.save.mockResolvedValueOnce(undefined)
  expect(await beforeExecute('post')).toEqual({ proceed: false, refreshState: true })

  allowedDocumentActions = new Set(['post', 'unpost', 'mark_for_deletion', 'unmark_for_deletion'])
  expect(state.canPost).toBe(true)
  expect(state.canUnpost).toBe(true)
  await (wrapper.vm as any).markForDeletion()
  await (wrapper.vm as any).unmarkForDeletion()
  await (wrapper.vm as any).post()
  await (wrapper.vm as any).unpost()

  expect(state.bulkUnitsOpen).toBe(false)
  state.openBulkCreateUnitsWizard()
  expect(state.bulkUnitsOpen).toBe(false)

  allowedDocumentActions = new Set(['unmark_for_deletion'])
  state.toggleMarkForDeletion()
  expect(mocks.requestDocumentAction).toHaveBeenLastCalledWith('unmark_for_deletion')
  allowedDocumentActions = new Set(['mark_for_deletion'])
  state.toggleMarkForDeletion()
  expect(mocks.requestDocumentAction).toHaveBeenLastCalledWith('mark_for_deletion')
  allowedDocumentActions.clear()
  state.toggleMarkForDeletion()

  allowedDocumentActions = new Set(['unpost'])
  state.togglePost()
  expect(mocks.requestDocumentAction).toHaveBeenLastCalledWith('unpost')
  allowedDocumentActions = new Set(['post'])
  state.togglePost()
  expect(mocks.requestDocumentAction).toHaveBeenLastCalledWith('post')
  allowedDocumentActions.clear()
  state.togglePost()

  expect(state.extraPageActions).toEqual([])
  expect((mocks.configuredArgs?.loading as { value: boolean }).value).toBe(false)
  expect((mocks.configuredArgs?.saving as { value: boolean }).value).toBe(false)
  expect((mocks.headerArgs?.loading as { value: boolean }).value).toBe(false)
  expect((mocks.headerArgs?.saving as { value: boolean }).value).toBe(true)
  expect((mocks.pageArgs?.loading as { value: boolean }).value).toBe(false)
  expect((mocks.pageArgs?.saving as { value: boolean }).value).toBe(false)
  expect((mocks.outputsArgs?.loading as { value: boolean }).value).toBe(false)
  expect((mocks.outputsArgs?.saving as { value: boolean }).value).toBe(false)
  expect((mocks.outputsArgs?.canExpand as { value: boolean }).value).toBe(true)
  expect((mocks.outputsArgs?.extraFlags as { value: unknown }).value).toEqual({ bulkCreateUnits: false })

  ;(mocks.leaveArgs?.onClose as () => void)()
  ;(mocks.adapterContext?.emitCreated as (id: string) => void)('created')
  ;(mocks.adapterContext?.emitSaved as () => void)()
  ;(mocks.adapterContext?.emitChanged as (reason: string) => void)('field')
  ;(mocks.adapterContext?.emitDeleted as () => void)()
  ;(mocks.persistenceArgs?.emitChanged as (reason: string) => void)('parts')
  ;(mocks.persistenceArgs?.emitDeleted as () => void)()
  await state.pageActionHandlers.copyShareLink()
  await state.pageActionHandlers.save()
  state.handleHeaderAction('save')
  expect(mocks.handleDocumentHeaderAction).toHaveBeenCalledWith('save')

  expect(state.afterFormExtensions).toHaveLength(1)
  const extension = state.afterFormExtensions[0]
  extension.componentRef({ validate: true })
  expect(state.leaseGridRef).toEqual({ validate: true })
  extension.componentRef(null)
  extension.props['onUpdate:modelValue']([{ party_id: 'p5' }])
  expect(state.leasePartiesRows).toEqual([{ party_id: 'p5' }])
  state.loading = true
  await nextTick()
  expect(state.afterFormExtensions).toEqual([])
  state.loading = false
  expect(state.dialogExtensions).toEqual([])

  mocks.refreshDocumentActions.mockRejectedValueOnce(new Error('refresh failed'))
  ;(mocks.configuredArgs?.applyActionDocument as (document: unknown) => void)({ status: 4, payload: { fields: {} } })
  await vi.waitFor(() => expect(mocks.refreshDocumentActions).toHaveBeenCalled())
  state.docEffects = { entries: [] }
  expect((wrapper.vm as any).getDocumentEffects()).toEqual({ entries: [] })
  state.currentId = null
  expect(await (wrapper.vm as any).reloadDocumentEffects()).toBeNull()
  state.currentId = 'lease-1'
  expect(await (wrapper.vm as any).reloadDocumentEffects()).toEqual({ entries: [] })
  expect(mocks.loadDocumentEffectsSnapshot).toHaveBeenCalledWith('pm.lease', 'lease-1')
  state.model = { changed: true }
  expect((wrapper.vm as any).getIsDirty()).toBe(true)
  expect((wrapper.vm as any).getCanSave()).toBe(true)
  expect((wrapper.vm as any).getFlags()).toEqual({})
  for (const button of wrapper.findAll('[data-editor-event]')) await button.trigger('click')
  wrapper.unmount()

  mocks.hasTag.mockReset().mockImplementation((tag: string) => tag === 'property')
  const property = mount(PmEntityEditor, {
    attachTo: document.body,
    props: { kind: 'catalog', typeCode: 'pm.property', id: 'property-1' },
  })
  await nextTick()
  const propertyState = editorState(property)
  propertyState.catalogMeta = { form: null }
  propertyState.catalogItem = { isMarkedForDeletion: false, isDeleted: true }
  propertyState.model = { display: 'Main property' }
  mocks.pmCanBulkCreateUnits!.value = true
  await nextTick()
  expect(propertyState.isPmPropertyCatalog).toBe(true)
  expect(propertyState.isLeaseDocument).toBe(false)
  await (property.vm as any).markForDeletion()
  await (property.vm as any).unmarkForDeletion()
  mocks.canUnmarkForDeletion!.value = true
  propertyState.toggleMarkForDeletion()
  mocks.canUnmarkForDeletion!.value = false
  mocks.canMarkForDeletion!.value = true
  propertyState.toggleMarkForDeletion()
  mocks.canMarkForDeletion!.value = false
  propertyState.toggleMarkForDeletion()
  expect(propertyState.extraPageActions).toHaveLength(1)
  expect(propertyState.extraPageActions[0].disabled).toBe(false)
  propertyState.loading = true
  expect(propertyState.extraPageActions[0].disabled).toBe(true)
  propertyState.loading = false
  propertyState.saving = true
  expect(propertyState.extraPageActions[0].disabled).toBe(true)
  propertyState.saving = false
  propertyState.openBulkCreateUnitsWizard()
  expect(propertyState.bulkUnitsOpen).toBe(true)
  expect(propertyState.dialogExtensions).toHaveLength(1)
  expect(propertyState.dialogExtensions[0].props).toMatchObject({ buildingId: 'property-1', buildingDisplay: 'Main property' })
  propertyState.dialogExtensions[0].props['onUpdate:open'](false)
  expect(propertyState.bulkUnitsOpen).toBe(false)
  propertyState.handleHeaderAction('openBulkCreateUnits')
  expect(mocks.runEntityEditorAction).toHaveBeenCalled()
  propertyState.resetInitialSnapshot()
  expect(propertyState.currentSnapshot).toContain('"parties":null')
  await property.find('[data-editor-event="back"]').trigger('click')
  property.unmount()

  const newProperty = mount(PmEntityEditor, { props: { kind: 'catalog', typeCode: 'pm.property' } })
  const newPropertyState = editorState(newProperty)
  mocks.pmCanBulkCreateUnits!.value = true
  newPropertyState.model = { display: null }
  expect(newPropertyState.dialogExtensions).toEqual([])
  expect(newPropertyState.currentId).toBeNull()
  newProperty.unmount()
})
