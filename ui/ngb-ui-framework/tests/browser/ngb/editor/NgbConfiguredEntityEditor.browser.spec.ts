import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
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
  persistenceArgs: null as Record<string, any> | null,
  persistenceContext: null as Record<string, any> | null,
  configuredArgs: null as Record<string, any> | null,
  headerArgs: null as Record<string, any> | null,
  navigationArgs: null as Record<string, any> | null,
  lifecycleArgs: null as Record<string, any> | null,
  commandPaletteArgs: null as Record<string, any> | null,
  shellProps: null as Record<string, any> | null,
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
  useEntityEditorBusinessContext: () => ({
    currentEditorContext: () => ({ kind: 'document', typeCode: 'demo.invoice' }),
  }),
}))

vi.mock('../../../../src/ngb/editor/useEntityEditorCapabilities', async () => {
  const { ref } = await import('vue')
  return {
    useEntityEditorCapabilities: () => ({
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
    }),
  }
})

vi.mock('../../../../src/ngb/editor/useEntityEditorLeaveGuard', async () => {
  const { ref } = await import('vue')
  return {
    useEntityEditorLeaveGuard: () => ({
      leaveOpen: ref(false),
      requestNavigate: mocks.requestNavigate,
      requestClose: mocks.requestClose,
      confirmLeave: vi.fn(),
      cancelLeave: vi.fn(),
    }),
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
      return {
        documentLifecycleActions: ref({ deletion: null, posting: null }),
        extraPrimaryActions: ref([]),
        extraMoreActionGroups: ref([]),
        handleConfiguredAction: mocks.handleConfiguredAction,
        requestDocumentAction: mocks.requestDocumentAction,
        isDocumentActionAllowed: mocks.isDocumentActionAllowed,
        confirmation: ref(null),
        cancelDocumentActionConfirmation: vi.fn(),
        confirmDocumentAction: vi.fn(),
        executingDocumentAction: ref(false),
        refreshDocumentActions: mocks.refreshDocumentActions,
      }
    },
  }
})

vi.mock('../../../../src/ngb/editor/useEntityEditorLifecycleConfirmations', async () => {
  const { ref } = await import('vue')
  return {
    useEntityEditorLifecycleConfirmations: (args: Record<string, any>) => {
      mocks.lifecycleArgs = args
      return {
        markConfirmOpen: ref(false),
        markConfirmMessage: ref('Confirm deletion'),
        requestMarkForDeletion: mocks.requestMarkForDeletion,
        cancelMarkForDeletion: vi.fn(),
        confirmMarkForDeletion: vi.fn(),
      }
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
  const { ref } = await import('vue')
  return { useEntityEditorPageActions: () => ref([]) }
})

vi.mock('../../../../src/ngb/editor/useEntityEditorOutputs', async () => {
  const { computed } = await import('vue')
  return { useEntityEditorOutputs: () => ({ flags: computed(() => ({ dirty: false })) }) }
})

vi.mock('../../../../src/ngb/editor/NgbEntityEditor.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return {
    default: defineComponent({
      name: 'NgbEntityEditor',
      inheritAttrs: false,
      props: {
        afterFormExtensions: { type: Array, default: () => [] },
      },
      emits: [
        'back', 'close', 'action', 'closeAuditLog', 'cancelLeave', 'confirmLeave',
        'cancelMarkForDeletion', 'confirmMarkForDeletion', 'cancelDocumentAction', 'confirmDocumentAction',
      ],
      setup(props, { attrs, emit, expose }) {
        expose({ focusField: (path: string) => path === 'known', focusFirstError: vi.fn(() => true) })
        return () => {
          mocks.shellProps = attrs
          return h('div', {
            'data-testid': 'entity-editor-shell',
            'data-extension-count': String(props.afterFormExtensions.length),
          }, [
            h('button', { 'data-testid': 'back', onClick: () => emit('back') }, 'back'),
            h('button', { 'data-testid': 'close', onClick: () => emit('close') }, 'close'),
            h('button', { 'data-testid': 'action', onClick: () => emit('action', 'save') }, 'action'),
          ])
        }
      },
    }),
  }
})

import NgbConfiguredEntityEditor from '../../../../src/ngb/editor/NgbConfiguredEntityEditor.vue'
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
  mocks.shellProps = null
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
  expect(handle.getCanSave()).toBe(true)
  expect(handle.getFlags()).toEqual({ dirty: false })
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

  ;(wrapper.vm as any).toggleMarkForDeletion()
  expect(mocks.requestMarkForDeletion).toHaveBeenCalled()
  await wrapper.get('[data-testid="action"]').trigger('click')
  expect(mocks.runEntityEditorAction).toHaveBeenCalledWith('save', expect.objectContaining({
    save: expect.any(Function),
    toggleMarkForDeletion: expect.any(Function),
  }))
  wrapper.unmount()
})
