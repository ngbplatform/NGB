import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { nextTick } from 'vue'

const mocks = vi.hoisted(() => ({
  configuredArgs: null as Record<string, unknown> | null,
  headerArgs: null as Record<string, unknown> | null,
  paletteArgs: null as Record<string, unknown> | null,
  handleConfiguredAction: vi.fn(),
  refreshDocumentActions: vi.fn(),
  post: vi.fn(),
  requestUnpost: vi.fn(),
  requestMarkForDeletion: vi.fn(),
  canPost: null as { value: boolean } | null,
  canUnpost: null as { value: boolean } | null,
}))

vi.mock('vue-router', async (importOriginal) => ({
  ...(await importOriginal<typeof import('vue-router')>()),
  useRoute: () => ({ name: 'document' }),
  useRouter: () => ({
    push: vi.fn(),
    replace: vi.fn(),
  }),
}))

vi.mock('../../src/editor/CRMDocumentPartsEditor.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent(() => () => h('div')) }
})

vi.mock('../../src/editor/useCatalogEntityEditorPersistence', () => ({
  useCatalogEntityEditorPersistence: vi.fn(() => ({})),
}))

vi.mock('../../src/editor/useDocumentEntityEditorPersistence', () => ({
  useDocumentEntityEditorPersistence: vi.fn(() => ({})),
}))

vi.mock('../../src/metadata/framework', () => ({
  crmMetadataFormBehavior: {},
}))

vi.mock('@ngbplatform/ui', async () => {
  const { computed, defineComponent, h, ref } = await import('vue')
  const yes = ref(true)
  const no = ref(false)
  const canPost = ref(true)
  const canUnpost = ref(true)
  const empty = ref('')
  const noop = vi.fn()
  mocks.canPost = canPost
  mocks.canUnpost = canUnpost

  return {
    NgbConfirmDialog: defineComponent(() => () => h('div')),
    NgbEntityEditor: defineComponent({
      props: {
        saving: Boolean,
      },
      setup(props) {
        return () => h('div', {
          'data-testid': 'editor-shell',
          'data-saving': String(props.saving),
        })
      },
    }),
    clonePlainData: (value: unknown) => structuredClone(value),
    navigateBack: noop,
    normalizeDocumentStatusValue: (value: unknown) => Number(value) || 1,
    normalizeEntityEditorError: () => ({ summary: 'error', issues: [] }),
    runEntityEditorAction: noop,
    stableStringify: (value: unknown) => JSON.stringify(value),
    humanizeEntityEditorFieldKey: (value: string) => value,
    isEntityEditorFormIssuePath: () => false,
    useMetadataStore: () => ({}),
    useLookupStore: () => ({}),
    useToasts: () => ({ push: noop }),
    useEntityEditorBusinessContext: () => ({
      currentEditorContext: () => ({ kind: 'document', typeCode: 'crm.lead_intake' }),
    }),
    useEntityEditorCapabilities: () => ({
      canOpenAudit: yes,
      canShareLink: yes,
      canOpenEffectsPage: yes,
      canOpenDocumentFlowPage: yes,
      canPrintDocument: yes,
      canMarkForDeletion: yes,
      canUnmarkForDeletion: no,
      canDelete: yes,
      canPost,
      canUnpost,
      canSave: yes,
      documentStatusLabel: empty,
      documentStatusTone: empty,
      title: ref('Lead'),
      subtitle: empty,
      auditEntityKind: empty,
      auditEntityId: empty,
      auditEntityTitle: empty,
      isReadOnly: no,
    }),
    useEntityEditorLeaveGuard: () => ({
      leaveOpen: no,
      requestNavigate: noop,
      requestClose: noop,
      confirmLeave: noop,
      cancelLeave: noop,
    }),
    useEntityEditorPersistence: () => ({
      load: vi.fn().mockResolvedValue(undefined),
      save: vi.fn().mockResolvedValue(undefined),
      markForDeletion: vi.fn().mockResolvedValue(undefined),
      unmarkForDeletion: vi.fn().mockResolvedValue(undefined),
      deleteEntity: vi.fn().mockResolvedValue(undefined),
      post: mocks.post,
      unpost: vi.fn().mockResolvedValue(undefined),
      loadDocumentEffectsSnapshot: vi.fn().mockResolvedValue(undefined),
    }),
    useEntityEditorNavigationActions: () => ({
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
    }),
    useConfiguredEntityEditorDocumentActions: (args: Record<string, unknown>) => {
      mocks.configuredArgs = args
      ;(args.applyActionDocument as (document: unknown) => void)({
        id: 'doc-1',
        status: 2,
        isMarkedForDeletion: false,
        payload: { fields: { memo: 'updated' } },
      })
      ;(args.applyActionDocument as (document: unknown) => void)({
        id: 'doc-1',
        status: 2,
        isMarkedForDeletion: false,
      })
      return {
        extraPrimaryActions: ref([]),
        extraMoreActionGroups: ref([]),
        handleConfiguredAction: mocks.handleConfiguredAction,
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
        handleDocumentHeaderAction: noop,
      }
    },
    useEntityEditorLifecycleConfirmations: () => ({
      markConfirmOpen: no,
      markConfirmMessage: empty,
      requestMarkForDeletion: mocks.requestMarkForDeletion,
      cancelMarkForDeletion: noop,
      confirmMarkForDeletion: noop,
      unpostConfirmOpen: no,
      requestUnpost: mocks.requestUnpost,
      cancelUnpost: noop,
      confirmUnpost: noop,
    }),
    useEntityEditorCommandPalette: (args: Record<string, unknown>) => {
      mocks.paletteArgs = args
      void (args.canPost as { value: boolean }).value
      void (args.canUnpost as { value: boolean }).value
    },
    useEntityEditorPageActions: () => ref([]),
    useEntityEditorOutputs: () => ({ flags: computed(() => ({})) }),
  }
})

import CRMEntityEditor from '../../src/editor/CRMEntityEditor.vue'

test('connects unified document actions to the CRM editor shell', async () => {
  mocks.refreshDocumentActions.mockReset().mockResolvedValue(undefined)
  mocks.post.mockReset().mockResolvedValue(undefined)
  mocks.requestUnpost.mockReset()
  mocks.requestMarkForDeletion.mockReset()
  mocks.canPost!.value = true
  mocks.canUnpost!.value = true
  const view = await render(CRMEntityEditor, {
    props: {
      kind: 'document',
      typeCode: 'crm.lead_intake',
      id: 'doc-1',
    },
  })

  await expect.element(view.getByTestId('editor-shell')).toHaveAttribute('data-saving', 'true')
  expect(mocks.configuredArgs).toMatchObject({
    applyActionDocument: expect.any(Function),
  })
  expect(mocks.headerArgs).toMatchObject({
    onTogglePost: expect.any(Function),
    onToggleMarkForDeletion: expect.any(Function),
  })
  expect(mocks.headerArgs).not.toHaveProperty('suppressBuiltInDocumentLifecycleActions')
  expect(mocks.handleConfiguredAction).toHaveBeenCalledWith('document-action:post')
  expect((mocks.paletteArgs?.canPost as { value: boolean }).value).toBe(true)
  expect((mocks.paletteArgs?.canUnpost as { value: boolean }).value).toBe(true)

  const onTogglePost = mocks.headerArgs?.onTogglePost as () => void
  onTogglePost()
  expect(mocks.requestUnpost).toHaveBeenCalledOnce()
  mocks.canUnpost!.value = false
  onTogglePost()
  expect(mocks.post).toHaveBeenCalledOnce()
  mocks.canPost!.value = false
  onTogglePost()
  expect(mocks.post).toHaveBeenCalledOnce()

  ;(mocks.headerArgs?.onToggleMarkForDeletion as () => void)()
  expect(mocks.requestMarkForDeletion).toHaveBeenCalledOnce()

  const applyDocument = mocks.configuredArgs?.applyActionDocument as (document: unknown) => void
  applyDocument({ id: 'doc-1', status: 3, payload: { fields: {} } })
  await vi.waitFor(() => expect(mocks.refreshDocumentActions).toHaveBeenCalledOnce())
  mocks.refreshDocumentActions.mockRejectedValueOnce(new Error('refresh failed'))
  applyDocument({ id: 'doc-1', status: 4, payload: { fields: {} } })
  await vi.waitFor(() => expect(mocks.refreshDocumentActions).toHaveBeenCalledTimes(2))
  view.unmount()

  const catalogView = await render(CRMEntityEditor, {
    props: { kind: 'catalog', typeCode: 'crm.account', id: 'catalog-1' },
  })
  ;(mocks.configuredArgs?.applyActionDocument as (document: unknown) => void)({
    id: 'catalog-1', status: 3, payload: { fields: {} },
  })
  await nextTick()
  expect(mocks.refreshDocumentActions).toHaveBeenCalledTimes(2)
  catalogView.unmount()

  const newDocumentView = await render(CRMEntityEditor, {
    props: { kind: 'document', typeCode: 'crm.lead_intake' },
  })
  ;(mocks.configuredArgs?.applyActionDocument as (document: unknown) => void)({
    id: 'draft', status: 3, payload: { fields: {} },
  })
  await nextTick()
  expect(mocks.refreshDocumentActions).toHaveBeenCalledTimes(2)
  newDocumentView.unmount()
})
