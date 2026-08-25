import { mount } from '@vue/test-utils'
import { computed, nextTick, ref } from 'vue'
import { beforeEach, expect, test, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  routerReplace: vi.fn(),
  routerPush: vi.fn(),
  navigateBack: vi.fn(),
  toastPush: vi.fn(),
  ensureCatalogType: vi.fn(),
  ensureDocumentType: vi.fn(),
  load: vi.fn(),
  save: vi.fn(),
  markForDeletion: vi.fn(),
  unmarkForDeletion: vi.fn(),
  deleteEntity: vi.fn(),
  loadDocumentEffectsSnapshot: vi.fn(),
  refreshDocumentActions: vi.fn(),
  requestDocumentAction: vi.fn(),
  isDocumentActionAllowed: vi.fn(),
  handleConfiguredAction: vi.fn(),
  handleDocumentHeaderAction: vi.fn(),
  runEntityEditorAction: vi.fn(),
  requestMarkForDeletion: vi.fn(),
  requestNavigate: vi.fn(),
  requestClose: vi.fn(),
  copyShareLink: vi.fn(),
  copyDocument: vi.fn(),
  openDocumentPrintPage: vi.fn(),
  openAuditLog: vi.fn(),
  closeAuditLog: vi.fn(),
  openDocumentEffectsPage: vi.fn(),
  openDocumentFlowPage: vi.fn(),
  openFullPage: vi.fn(),
  openCompactPage: vi.fn(),
  closePage: vi.fn(),
  confirmLeave: vi.fn(),
  cancelLeave: vi.fn(),
  cancelMarkForDeletion: vi.fn(),
  confirmMarkForDeletion: vi.fn(),
  cancelDocumentActionConfirmation: vi.fn(),
  confirmDocumentAction: vi.fn(),
  focusField: vi.fn(),
  focusFirstError: vi.fn(),
  persistenceArgs: null as Record<string, any> | null,
  persistenceContext: null as Record<string, any> | null,
  configuredArgs: null as Record<string, any> | null,
  headerArgs: null as Record<string, any> | null,
  navigationArgs: null as Record<string, any> | null,
  lifecycleArgs: null as Record<string, any> | null,
  commandPaletteArgs: null as Record<string, any> | null,
  pageActionsArgs: null as Record<string, any> | null,
  outputsArgs: null as Record<string, any> | null,
  leaveArgs: null as Record<string, any> | null,
  shellProps: null as Record<string, any> | null,
  capabilities: null as Record<string, any> | null,
  leaveState: null as Record<string, any> | null,
  configuredState: null as Record<string, any> | null,
  lifecycleState: null as Record<string, any> | null,
  businessArgs: null as Record<string, any> | null,
}))

vi.mock('vue-router', async (importOriginal) => ({
  ...(await importOriginal<typeof import('vue-router')>()),
  useRoute: () => ({ fullPath: '/documents/demo.invoice/current', query: {}, hash: '' }),
  useRouter: () => ({ replace: mocks.routerReplace, push: mocks.routerPush }),
}))

vi.mock('../../../../src/ngb/lookup/store', () => ({
  useLookupStore: () => ({ resolve: vi.fn() }),
}))

vi.mock('../../../../src/ngb/metadata/store', () => ({
  useMetadataStore: () => ({
    ensureCatalogType: mocks.ensureCatalogType,
    ensureDocumentType: mocks.ensureDocumentType,
  }),
}))

vi.mock('../../../../src/ngb/primitives/toast', () => ({
  useToasts: () => ({ push: mocks.toastPush }),
}))

vi.mock('../../../../src/ngb/router/backNavigation', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../../src/ngb/router/backNavigation')>()),
  navigateBack: mocks.navigateBack,
}))

vi.mock('../../../../src/ngb/editor/extensions', () => ({
  runEntityEditorAction: mocks.runEntityEditorAction,
}))

vi.mock('../../../../src/ngb/editor/useEntityEditorBusinessContext', () => ({
  useEntityEditorBusinessContext: (args: Record<string, any>) => {
    mocks.businessArgs = args
    return { currentEditorContext: () => ({ kind: 'document', typeCode: 'demo.invoice' }) }
  },
}))

vi.mock('../../../../src/ngb/editor/useEntityEditorCapabilities', async () => {
  return {
    useEntityEditorCapabilities: () => mocks.capabilities,
  }
})

vi.mock('../../../../src/ngb/editor/useEntityEditorLeaveGuard', async () => {
  return {
    useEntityEditorLeaveGuard: (args: Record<string, any>) => {
      mocks.leaveArgs = args
      return mocks.leaveState
    },
  }
})

vi.mock('../../../../src/ngb/editor/entityEditorPersistence', () => ({
  useEntityEditorPersistence: (args: Record<string, any>) => {
    mocks.persistenceArgs = args
    return {
      load: mocks.load,
      save: mocks.save,
      markForDeletion: mocks.markForDeletion,
      unmarkForDeletion: mocks.unmarkForDeletion,
      deleteEntity: mocks.deleteEntity,
      loadDocumentEffectsSnapshot: mocks.loadDocumentEffectsSnapshot,
    }
  },
}))

vi.mock('../../../../src/ngb/editor/useEntityEditorNavigationActions', async () => {
  const { ref } = await import('vue')
  return {
    useEntityEditorNavigationActions: (args: Record<string, any>) => {
      mocks.navigationArgs = args
      return {
        auditOpen: ref(false),
        fallbackCloseTarget: '/fallback',
        copyShareLink: mocks.copyShareLink,
        copyDocument: mocks.copyDocument,
        openDocumentPrintPage: mocks.openDocumentPrintPage,
        openAuditLog: mocks.openAuditLog,
        closeAuditLog: mocks.closeAuditLog,
        openDocumentEffectsPage: mocks.openDocumentEffectsPage,
        openDocumentFlowPage: mocks.openDocumentFlowPage,
        openFullPage: mocks.openFullPage,
        openCompactPage: mocks.openCompactPage,
        closePage: mocks.closePage,
      }
    },
  }
})

vi.mock('../../../../src/ngb/editor/useConfiguredEntityEditorDocumentActions', async () => {
  const { ref } = await import('vue')
  return {
    useConfiguredEntityEditorDocumentActions: (args: Record<string, any>) => {
      mocks.configuredArgs = args
      mocks.configuredState = {
        documentLifecycleActions: ref({ deletion: null, posting: null }),
        extraPrimaryActions: ref([]),
        extraMoreActionGroups: ref([]),
        handleConfiguredAction: mocks.handleConfiguredAction,
        requestDocumentAction: mocks.requestDocumentAction,
        isDocumentActionAllowed: mocks.isDocumentActionAllowed,
        confirmation: ref(null),
        cancelDocumentActionConfirmation: mocks.cancelDocumentActionConfirmation,
        confirmDocumentAction: mocks.confirmDocumentAction,
        executingDocumentAction: ref(false),
        refreshDocumentActions: mocks.refreshDocumentActions,
      }
      return mocks.configuredState
    },
  }
})

vi.mock('../../../../src/ngb/editor/useEntityEditorLifecycleConfirmations', async () => {
  const { ref } = await import('vue')
  return {
    useEntityEditorLifecycleConfirmations: (args: Record<string, any>) => {
      mocks.lifecycleArgs = args
      mocks.lifecycleState = {
        markConfirmOpen: ref(false),
        markConfirmMessage: ref('Confirm deletion'),
        requestMarkForDeletion: mocks.requestMarkForDeletion,
        cancelMarkForDeletion: mocks.cancelMarkForDeletion,
        confirmMarkForDeletion: mocks.confirmMarkForDeletion,
      }
      return mocks.lifecycleState
    },
  }
})

vi.mock('../../../../src/ngb/editor/useEntityEditorHeaderActions', async () => {
  const { ref } = await import('vue')
  return {
    useEntityEditorHeaderActions: (args: Record<string, any>) => {
      mocks.headerArgs = args
      return {
        documentPrimaryActions: ref([]),
        documentMoreActionGroups: ref([]),
        handleDocumentHeaderAction: mocks.handleDocumentHeaderAction,
      }
    },
  }
})

vi.mock('../../../../src/ngb/editor/useEntityEditorCommandPalette', () => ({
  useEntityEditorCommandPalette: (args: Record<string, any>) => {
    mocks.commandPaletteArgs = args
  },
}))

vi.mock('../../../../src/ngb/editor/useEntityEditorPageActions', async () => {
  const { computed } = await import('vue')
  return { useEntityEditorPageActions: (args: Record<string, any>) => {
    mocks.pageActionsArgs = args
    return computed(() => [{ key: `loading-${String(args.loading.value)}`, title: 'State', icon: 'save' }])
  } }
})

vi.mock('../../../../src/ngb/editor/useEntityEditorOutputs', async () => {
  const { computed } = await import('vue')
  return { useEntityEditorOutputs: (args: Record<string, any>) => {
    mocks.outputsArgs = args
    return {
      flags: computed(() => ({
        dirty: args.isDirty.value,
        loading: args.loading.value,
        saving: args.saving.value,
        canExpand: args.canExpand.value,
        canPost: args.canPost.value,
        canUnpost: args.canUnpost.value,
      })),
    }
  } }
})

vi.mock('../../../../src/ngb/editor/NgbEntityEditor.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return {
    default: defineComponent({
      name: 'NgbEntityEditor',
      inheritAttrs: false,
      props: {
        afterFormExtensions: { type: Array, default: () => [] },
        bannerIssues: { type: Array, default: () => [] },
        displayedError: { type: Object, default: null },
        errors: { type: Object, default: () => ({}) },
        saving: { type: Boolean, default: false },
      },
      emits: [
        'back', 'close', 'action', 'closeAuditLog', 'cancelLeave', 'confirmLeave',
        'cancelMarkForDeletion', 'confirmMarkForDeletion', 'cancelDocumentAction', 'confirmDocumentAction',
      ],
      setup(props, { attrs, emit, expose }) {
        expose({ focusField: mocks.focusField, focusFirstError: mocks.focusFirstError })
        return () => {
          mocks.shellProps = {
            ...attrs,
            afterFormExtensions: props.afterFormExtensions,
            bannerIssues: props.bannerIssues,
            displayedError: props.displayedError,
            errors: props.errors,
            saving: props.saving,
          }
          return h('div', {
            'data-testid': 'entity-editor-shell',
            'data-extension-count': String(props.afterFormExtensions.length),
          }, [
            h('button', { 'data-testid': 'back', onClick: () => emit('back') }, 'back'),
            h('button', { 'data-testid': 'close', onClick: () => emit('close') }, 'close'),
            h('button', { 'data-testid': 'action', onClick: () => emit('action', 'save') }, 'action'),
            h('button', { 'data-testid': 'close-audit', onClick: () => emit('closeAuditLog') }, 'close audit'),
            h('button', { 'data-testid': 'cancel-leave', onClick: () => emit('cancelLeave') }, 'cancel leave'),
            h('button', { 'data-testid': 'confirm-leave', onClick: () => emit('confirmLeave') }, 'confirm leave'),
            h('button', { 'data-testid': 'cancel-mark', onClick: () => emit('cancelMarkForDeletion') }, 'cancel mark'),
            h('button', { 'data-testid': 'confirm-mark', onClick: () => emit('confirmMarkForDeletion') }, 'confirm mark'),
            h('button', { 'data-testid': 'cancel-document-action', onClick: () => emit('cancelDocumentAction') }, 'cancel document action'),
            h('button', { 'data-testid': 'confirm-document-action', onClick: () => emit('confirmDocumentAction') }, 'confirm document action'),
          ])
        }
      },
    }),
  }
})

import NgbConfiguredEntityEditor from '../../../../src/ngb/editor/NgbConfiguredEntityEditor.vue'
import { ApiError } from '../../../../src/ngb/api/http'
import type { ConfiguredEntityEditorConfiguration } from '../../../../src/ngb/editor/configuredEntityEditor'

const DocumentPartsStub = { name: 'DocumentPartsStub', render: () => null }

function configuration(): ConfiguredEntityEditorConfiguration {
  return {
    documentPartsExtensionKey: 'test-parts',
    documentPartsEditor: DocumentPartsStub,
    metadataFormBehavior: {},
    createCatalogPersistence: (context) => {
      mocks.persistenceContext = context
      return { load: vi.fn(), save: vi.fn(), markForDeletion: vi.fn(), unmarkForDeletion: vi.fn(), deleteEntity: vi.fn() }
    },
    createDocumentPersistence: (context) => {
      mocks.persistenceContext = context
      return { load: vi.fn(), save: vi.fn() }
    },
  }
}

beforeEach(() => {
  vi.clearAllMocks()
  mocks.persistenceArgs = null
  mocks.persistenceContext = null
  mocks.configuredArgs = null
  mocks.headerArgs = null
  mocks.navigationArgs = null
  mocks.lifecycleArgs = null
  mocks.commandPaletteArgs = null
  mocks.pageActionsArgs = null
  mocks.outputsArgs = null
  mocks.leaveArgs = null
  mocks.shellProps = null
  mocks.configuredState = null
  mocks.lifecycleState = null
  mocks.businessArgs = null
  mocks.focusField.mockImplementation((path: string) => path === 'known')
  mocks.focusFirstError.mockReturnValue(true)
  mocks.capabilities = {
    canOpenAudit: ref(true),
    canShareLink: ref(true),
    canOpenEffectsPage: ref(true),
    canOpenDocumentFlowPage: ref(true),
    canPrintDocument: ref(true),
    canMarkForDeletion: ref(true),
    canUnmarkForDeletion: ref(false),
    canDelete: ref(true),
    canSave: ref(true),
    documentStatusLabel: ref('Draft'),
    documentStatusTone: ref('neutral'),
    title: ref('Configured document'),
    subtitle: ref('Subtitle'),
    auditEntityKind: ref('document'),
    auditEntityId: ref('document-id'),
    auditEntityTitle: ref('Configured document'),
    isReadOnly: ref(false),
  }
  mocks.leaveState = {
    leaveOpen: ref(false),
    requestNavigate: mocks.requestNavigate,
    requestClose: mocks.requestClose,
    confirmLeave: mocks.confirmLeave,
    cancelLeave: mocks.cancelLeave,
  }
  mocks.isDocumentActionAllowed.mockImplementation((code: string) => code === 'post' || code === 'mark_for_deletion')
  mocks.refreshDocumentActions.mockResolvedValue(undefined)
  mocks.save.mockResolvedValue(undefined)
  mocks.markForDeletion.mockResolvedValue(undefined)
  mocks.unmarkForDeletion.mockResolvedValue(undefined)
  mocks.deleteEntity.mockResolvedValue(undefined)
  mocks.loadDocumentEffectsSnapshot.mockResolvedValue(undefined)
  mocks.copyShareLink.mockResolvedValue(undefined)
  mocks.routerReplace.mockResolvedValue(undefined)
  mocks.ensureCatalogType.mockResolvedValue({ catalogType: 'demo.customer', displayName: 'Customer' })
  mocks.ensureDocumentType.mockResolvedValue({ documentType: 'demo.invoice', displayName: 'Invoice' })
})

test('orchestrates document state, actions, extensions, navigation, and persistence through ports', async () => {
  const onCreated = vi.fn()
  const onSaved = vi.fn()
  const onChanged = vi.fn()
  const wrapper = mount(NgbConfiguredEntityEditor, {
    props: {
      kind: 'document',
      typeCode: 'demo.invoice',
      id: 'document-id',
      initialFields: { memo: 'initial' },
      configuration: configuration(),
      onCreated,
      onSaved,
      onChanged,
    },
  })
  await nextTick()

  expect(mocks.load).toHaveBeenCalled()
  expect(mocks.persistenceArgs).toMatchObject({
    adapters: { catalog: expect.any(Object), document: expect.any(Object) },
    onMarkedForDeletion: expect.any(Function),
    onUnmarkedForDeletion: expect.any(Function),
  })
  expect(mocks.configuredArgs).toMatchObject({
    beforeExecute: expect.any(Function),
    applyActionDocument: expect.any(Function),
  })
  expect(mocks.headerArgs).toMatchObject({ onUnhandledAction: expect.any(Function) })
  expect(mocks.commandPaletteArgs).toMatchObject({
    isDocumentActionAllowed: mocks.isDocumentActionAllowed,
    requestDocumentAction: mocks.requestDocumentAction,
  })
  expect(mocks.configuredArgs!.currentId.value).toBe('document-id')
  expect(mocks.businessArgs!.isDraft.value).toBe(true)
  expect(mocks.navigationArgs!.buildCopyParts()).toBeNull()
  expect(mocks.pageActionsArgs!.saving.value).toBe(false)

  await mocks.persistenceContext!.ensureDocumentMetadata('demo.invoice')
  expect(mocks.ensureDocumentType).toHaveBeenCalledWith('demo.invoice')
  await mocks.persistenceContext!.onCreated('new-document-id')
  mocks.persistenceContext!.onSaved()
  expect(onCreated).toHaveBeenCalledWith('new-document-id')
  expect(onSaved).toHaveBeenCalledOnce()
  expect(mocks.routerReplace).toHaveBeenCalledWith('/documents/demo.invoice/new-document-id')

  mocks.configuredArgs!.applyActionDocument({
    id: 'document-id',
    status: 2,
    isMarkedForDeletion: false,
    payload: { fields: { memo: 'posted' } },
  })
  await nextTick()
  expect(onChanged).toHaveBeenCalled()
  expect(mocks.refreshDocumentActions).toHaveBeenCalled()

  mocks.persistenceContext!.docMeta.value = {
    documentType: 'demo.invoice',
    displayName: 'Invoice',
    parts: [{ partCode: 'lines', title: 'Lines', list: { columns: [] } }],
  }
  await nextTick()
  expect(wrapper.get('[data-testid="entity-editor-shell"]').attributes('data-extension-count')).toBe('1')

  await mocks.persistenceArgs!.onMarkedForDeletion()
  await mocks.persistenceArgs!.onUnmarkedForDeletion()
  expect(mocks.toastPush).toHaveBeenCalledWith(expect.objectContaining({ title: 'Deleted' }))
  expect(mocks.toastPush).toHaveBeenCalledWith(expect.objectContaining({ title: 'Restored' }))

  await wrapper.get('[data-testid="back"]').trigger('click')
  await wrapper.get('[data-testid="close"]').trigger('click')
  await wrapper.get('[data-testid="action"]').trigger('click')
  expect(mocks.navigateBack).toHaveBeenCalled()
  expect(mocks.closePage).toHaveBeenCalled()
  expect(mocks.handleDocumentHeaderAction).toHaveBeenCalledWith('save')

  const handle = wrapper.vm as any
  handle.toggleMarkForDeletion()
  mocks.isDocumentActionAllowed.mockImplementation((code: string) => code === 'unmark_for_deletion' || code === 'unpost')
  handle.toggleMarkForDeletion()
  handle.togglePost()
  mocks.isDocumentActionAllowed.mockReturnValue(false)
  handle.togglePost()
  await handle.markForDeletion()
  await handle.unmarkForDeletion()
  await handle.deleteEntity()
  await handle.post()
  await handle.unpost()
  await handle.copyShareLink()
  handle.copyDocument()
  handle.printDocument()
  handle.openAuditLog()
  expect(mocks.requestDocumentAction).toHaveBeenCalledWith('post')
  expect(mocks.requestDocumentAction).toHaveBeenCalledWith('mark_for_deletion')
  expect(mocks.requestDocumentAction).toHaveBeenCalledWith('unmark_for_deletion')
  expect(mocks.deleteEntity).toHaveBeenCalled()
  mocks.persistenceContext!.docEffects.value = { accountingEntries: [], operationalRegisterMovements: [], referenceRegisterWrites: [] }
  expect(await handle.reloadDocumentEffects()).toEqual({ accountingEntries: [], operationalRegisterMovements: [], referenceRegisterWrites: [] })
  expect(mocks.loadDocumentEffectsSnapshot).toHaveBeenCalledWith('demo.invoice', 'document-id')
  await wrapper.setProps({ id: null })
  await nextTick()
  mocks.configuredArgs!.applyActionDocument({ id: 'document-id', status: 3, payload: { fields: {} } })
  await nextTick()
  expect(await handle.reloadDocumentEffects()).toBeNull()
  expect(handle.getCanSave()).toBe(true)
  expect(handle.getFlags()).toMatchObject({ dirty: false, loading: false, saving: false, canExpand: false })
  wrapper.unmount()
})

test('keeps catalog lifecycle UI and creation navigation in the orchestration layer', async () => {
  const wrapper = mount(NgbConfiguredEntityEditor, {
    props: {
      kind: 'catalog',
      typeCode: 'demo.customer',
      mode: 'drawer',
      navigateOnCreate: false,
      configuration: configuration(),
    },
  })
  await nextTick()

  await mocks.persistenceContext!.ensureCatalogMetadata('demo.customer')
  await mocks.persistenceContext!.onCreated('customer-id')
  expect(mocks.ensureCatalogType).toHaveBeenCalledWith('demo.customer')
  expect(mocks.routerReplace).not.toHaveBeenCalled()
  expect(mocks.navigationArgs!.buildCopyParts()).toBeNull()

  ;(wrapper.vm as any).toggleMarkForDeletion()
  expect(mocks.requestMarkForDeletion).toHaveBeenCalled()
  await wrapper.get('[data-testid="action"]').trigger('click')
  expect(mocks.runEntityEditorAction).toHaveBeenCalledWith('save', expect.objectContaining({
    save: expect.any(Function),
    toggleMarkForDeletion: expect.any(Function),
  }))
  wrapper.unmount()
})

test('projects field and part validation, focuses actionable errors, and updates document parts', async () => {
  const wrapper = mount(NgbConfiguredEntityEditor, {
    props: {
      kind: 'document',
      typeCode: 'demo.invoice',
      id: 'document-id',
      initialParts: { lines: { rows: [] } },
      configuration: configuration(),
    },
  })
  await nextTick()

  mocks.persistenceContext!.setEditorError({
    summary: 'Before metadata',
    issues: [{ path: 'unknown', label: 'Unknown', scope: 'field', messages: ['Unknown'] }],
  })
  await nextTick()
  await nextTick()
  expect(mocks.shellProps!.errors).toEqual({})

  mocks.persistenceContext!.docMeta.value = {
    documentType: 'demo.invoice',
    displayName: 'Invoice',
    kind: 2,
    form: {
      sections: [
        { title: 'Main', rows: [{ fields: [
          { key: 'known', label: 'Known Label', dataType: 'String', uiControl: 1, isRequired: false, isReadOnly: false },
          { key: 'blank_label', label: '   ', dataType: 'String', uiControl: 1, isRequired: false, isReadOnly: false },
        ] }] },
      ],
    },
    parts: [{ partCode: 'lines', title: 'Lines', list: { columns: [] } }],
  }
  await nextTick()

  mocks.persistenceContext!.setEditorError({
    summary: 'Invalid',
    issues: [
      { path: '_form', label: 'Validation', scope: 'form', messages: ['Form issue'] },
      { path: 'ignored_scope', label: 'Ignored', scope: 'record', messages: ['Ignored'] },
      { path: 'unknown', label: 'Unknown', scope: 'field', messages: ['Unknown issue'] },
      { path: 'known', label: 'Known Label', scope: 'field', messages: ['', '   '] },
      { path: 'known', label: 'Known Label', scope: 'field', messages: ['', 'Required'] },
      { path: 'known', label: 'Known Label', scope: 'field', messages: ['Duplicate'] },
      { path: 'parts.lines.rows[0].amount', label: 'Amount', scope: 'field', messages: ['', 'Amount required'] },
      { path: 'parts.lines.rows[0].amount', label: 'Amount', scope: 'field', messages: ['Duplicate amount'] },
      { path: 'parts.lines.rows[1].memo', label: 'Memo', scope: 'field', messages: [] },
      { path: `parts.lines.rows[${'9'.repeat(400)}].memo`, label: 'Memo', scope: 'field', messages: ['Too large'] },
      { path: 'parts.invalid', label: 'Invalid', scope: 'field', messages: ['Invalid part path'] },
    ],
  })
  await nextTick()
  await nextTick()

  expect(mocks.shellProps!.errors).toEqual({ known: 'Required' })
  expect(mocks.shellProps!.bannerIssues).toHaveLength(11)
  const extension = mocks.shellProps!.afterFormExtensions[0]
  expect(extension.props.errors).toEqual({ lines: { 0: { amount: 'Amount required' } } })
  extension.props['onUpdate:modelValue']({ lines: { rows: [{ amount: 12 }] } })
  expect(mocks.persistenceContext!.partsModel.value).toEqual({ lines: { rows: [{ amount: 12 }] } })
  expect(mocks.focusField).toHaveBeenCalledWith('unknown')
  expect(mocks.focusField).toHaveBeenCalledWith('known')

  const normalized = mocks.persistenceArgs!.normalizeEditorError(new ApiError({
    message: 'Validation failed',
    status: 400,
    url: '/documents/demo.invoice',
    body: {
      issues: [
        { path: '', scope: 'form', message: 'Form invalid' },
        { path: 'known', scope: 'field', message: 'Known invalid' },
        { path: 'unknown_key', scope: 'field', message: 'Unknown invalid' },
      ],
    },
  }))
  expect(normalized.issues.map((issue: any) => issue.label)).toEqual(['Validation', 'Known Label', 'Unknown Key'])

  mocks.focusField.mockReturnValue(false)
  mocks.persistenceContext!.setEditorError({
    summary: 'Known invalid',
    issues: [{ path: 'known', label: 'Known Label', scope: 'field', messages: ['Still required'] }],
  })
  await nextTick()
  await nextTick()
  expect(mocks.focusFirstError).toHaveBeenCalledWith(['known'])

  mocks.persistenceContext!.setEditorError(null)
  await nextTick()
  expect(mocks.shellProps!.displayedError).toBeNull()
  wrapper.unmount()
})

test('normalizes labels, saves dirty documents before posting, and handles refresh failures', async () => {
  const onChanged = vi.fn()
  const wrapper = mount(NgbConfiguredEntityEditor, {
    props: {
      kind: 'document',
      typeCode: 'demo.invoice',
      id: 'document-id',
      initialFields: { memo: 'initial' },
      initialParts: { lines: { rows: [] } },
      expandTo: '/expanded',
      compactTo: '/compact',
      closeTo: '/closed',
      configuration: configuration(),
      onChanged,
    },
  })
  await nextTick()

  expect(mocks.persistenceContext!.initialFields.value).toEqual({ memo: 'initial' })
  expect(mocks.persistenceContext!.initialParts.value).toEqual({ lines: { rows: [] } })
  expect(mocks.navigationArgs!.expandTo.value).toBe('/expanded')
  expect(mocks.navigationArgs!.compactTo.value).toBe('/compact')
  expect(mocks.navigationArgs!.closeTo.value).toBe('/closed')
  expect(mocks.navigationArgs!.buildCopyParts()).toEqual({ lines: { rows: [] } })
  expect(mocks.configuredArgs!.loading.value).toBe(false)
  expect(mocks.configuredArgs!.saving.value).toBe(false)
  expect(mocks.headerArgs!.loading.value).toBe(false)
  expect(mocks.headerArgs!.saving.value).toBe(false)

  mocks.persistenceContext!.docMeta.value = {
    documentType: 'demo.invoice', displayName: 'Invoice', kind: 2,
    form: { sections: [{ title: 'Main', rows: [{ fields: [{ key: 'known', label: 'Known Label', dataType: 'String', uiControl: 1, isRequired: false, isReadOnly: false }] }] }] },
    parts: [],
  }
  const normalized = mocks.persistenceArgs!.normalizeEditorError(new Error('ordinary failure'))
  expect(normalized.summary).toBe('ordinary failure')

  mocks.persistenceContext!.model.value = { known: 'before' }
  mocks.persistenceContext!.resetInitialSnapshot()
  expect((wrapper.vm as any).getIsDirty()).toBe(false)
  mocks.persistenceContext!.model.value = { known: 'after' }
  await nextTick()
  expect((wrapper.vm as any).getIsDirty()).toBe(true)
  expect(await mocks.configuredArgs!.beforeExecute('copy')).toBe(true)

  mocks.save.mockImplementationOnce(async () => {
    mocks.persistenceContext!.setEditorError({ summary: 'Save failed', issues: [] })
  })
  expect(await mocks.configuredArgs!.beforeExecute('post')).toEqual({ proceed: false, refreshState: true })
  mocks.persistenceContext!.setEditorError(null)
  mocks.save.mockImplementationOnce(async () => mocks.persistenceContext!.resetInitialSnapshot())
  expect(await mocks.configuredArgs!.beforeExecute('post')).toEqual({ proceed: true, refreshState: true })

  mocks.refreshDocumentActions.mockRejectedValueOnce(new Error('Actions unavailable'))
  mocks.configuredArgs!.applyActionDocument({ id: 'document-id', status: 2, payload: undefined })
  await nextTick()
  await nextTick()
  expect(onChanged).toHaveBeenCalled()
  expect(mocks.shellProps!.displayedError?.summary).toBe('Actions unavailable')

  mocks.headerArgs!.onUnhandledAction('custom-action')
  expect(mocks.handleConfiguredAction).toHaveBeenCalledWith('custom-action')
  mocks.configuredState!.executingDocumentAction.value = true
  await nextTick()
  expect(mocks.shellProps!.saving).toBe(true)
  wrapper.unmount()
})

test('covers every shell event, lifecycle branch, creation route, and exposed handle boundary', async () => {
  const onClose = vi.fn()
  const onDeleted = vi.fn()
  const onChanged = vi.fn()
  const wrapper = mount(NgbConfiguredEntityEditor, {
    props: {
      kind: 'catalog',
      typeCode: 'demo.customer',
      id: 'customer-id',
      expandTo: '/catalog-expanded',
      configuration: configuration(),
      onClose,
      onDeleted,
      onChanged,
    },
  })
  await nextTick()

  mocks.persistenceArgs!.emitChanged('markForDeletion')
  mocks.persistenceArgs!.emitDeleted()
  expect(onChanged).toHaveBeenCalledWith('markForDeletion')
  expect(onDeleted).toHaveBeenCalledOnce()
  mocks.leaveArgs!.onClose()
  expect(onClose).toHaveBeenCalledOnce()

  for (const id of ['close-audit', 'cancel-leave', 'confirm-leave', 'cancel-mark', 'confirm-mark', 'cancel-document-action', 'confirm-document-action']) {
    await wrapper.get(`[data-testid="${id}"]`).trigger('click')
  }
  expect(mocks.closeAuditLog).toHaveBeenCalledOnce()
  expect(mocks.cancelLeave).toHaveBeenCalledOnce()
  expect(mocks.confirmLeave).toHaveBeenCalledOnce()
  expect(mocks.cancelMarkForDeletion).toHaveBeenCalledOnce()
  expect(mocks.confirmMarkForDeletion).toHaveBeenCalledOnce()
  expect(mocks.cancelDocumentActionConfirmation).toHaveBeenCalledOnce()
  expect(mocks.confirmDocumentAction).toHaveBeenCalledOnce()

  mocks.capabilities.canUnmarkForDeletion.value = true
  ;(wrapper.vm as any).toggleMarkForDeletion()
  expect(mocks.unmarkForDeletion).toHaveBeenCalledOnce()
  mocks.capabilities.canUnmarkForDeletion.value = false
  mocks.capabilities.canMarkForDeletion.value = false
  ;(wrapper.vm as any).toggleMarkForDeletion()
  expect(mocks.requestMarkForDeletion).not.toHaveBeenCalled()
  await (wrapper.vm as any).markForDeletion()
  await (wrapper.vm as any).unmarkForDeletion()
  expect(mocks.markForDeletion).toHaveBeenCalledOnce()
  expect(mocks.unmarkForDeletion).toHaveBeenCalledTimes(2)

  mocks.runEntityEditorAction.mockClear()
  await wrapper.get('[data-testid="action"]').trigger('click')
  const handlers = mocks.runEntityEditorAction.mock.calls[0]![1]
  await handlers.copyShareLink()
  await handlers.save()
  expect(mocks.copyShareLink).toHaveBeenCalled()
  expect(mocks.save).toHaveBeenCalled()

  await mocks.persistenceContext!.onCreated('created-customer')
  expect(mocks.routerReplace).toHaveBeenCalledWith('/catalogs/demo.customer/created-customer')
  const handle = wrapper.vm as any
  expect(handle.getDocumentEffects()).toBeNull()
  expect(await handle.reloadDocumentEffects()).toBeNull()
  handle.openFullPage()
  handle.openCompactPage()
  handle.closePage()
  handle.closeAuditLog()
  handle.openAudit()
  expect(handle.getFlags()).toMatchObject({ canExpand: true, canPost: true, canUnpost: false })

  await wrapper.setProps({ id: null })
  await nextTick()
  expect(await handle.reloadDocumentEffects()).toBeNull()
  wrapper.unmount()
})
