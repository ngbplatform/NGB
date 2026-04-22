import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { computed, defineComponent, h, ref } from 'vue'

import {
  StubBadge,
  StubHeaderActionCluster,
  StubIcon,
  StubPageHeader,
} from './stubs'

vi.mock('../../../../src/ngb/components/NgbHeaderActionCluster.vue', () => ({
  default: StubHeaderActionCluster,
}))

vi.mock('../../../../src/ngb/primitives/NgbBadge.vue', () => ({
  default: StubBadge,
}))

vi.mock('../../../../src/ngb/primitives/NgbIcon.vue', () => ({
  default: StubIcon,
}))

vi.mock('../../../../src/ngb/site/NgbPageHeader.vue', () => ({
  default: StubPageHeader,
}))

import { configureNgbEditor } from '../../../../src/ngb/editor/config'
import NgbEntityEditorHeader from '../../../../src/ngb/editor/NgbEntityEditorHeader.vue'
import { useConfiguredEntityEditorDocumentActions } from '../../../../src/ngb/editor/useConfiguredEntityEditorDocumentActions'
import { useEntityEditorHeaderActions } from '../../../../src/ngb/editor/useEntityEditorHeaderActions'

const configuredActionMocks = vi.hoisted(() => ({
  approve: vi.fn(),
  emailPacket: vi.fn(),
}))

const metadataStore = {
  ensureDocumentType: vi.fn(async (documentType: string) => ({
    documentType,
    displayName: 'Sales Invoice',
    kind: 2,
    list: null,
    form: null,
    parts: null,
    actions: null,
    presentation: null,
    capabilities: null,
  })),
}

const ConfiguredActionsHarness = defineComponent({
  setup() {
    const loading = ref(false)
    const saving = ref(false)
    const navigateLog = ref<string[]>([])

    function requestNavigate(to: string | null | undefined) {
      navigateLog.value = [...navigateLog.value, String(to ?? '')]
    }

    const {
      extraPrimaryActions,
      extraMoreActionGroups,
      handleConfiguredAction,
    } = useConfiguredEntityEditorDocumentActions({
      kind: computed(() => 'document'),
      typeCode: computed(() => 'pm.invoice'),
      currentId: computed(() => 'doc-1'),
      model: ref({
        customer_id: 'customer-1',
      }),
      uiEffects: computed(() => ({
        isPosted: false,
        canEdit: true,
        canPost: false,
        canUnpost: false,
        canRepost: false,
        canApply: false,
      })),
      loading: computed(() => loading.value),
      saving: computed(() => saving.value),
      requestNavigate,
      metadataStore,
      setEditorError: () => undefined,
      normalizeEditorError: () => ({ summary: 'normalized', issues: [] }),
      loadDerivationActions: async () => [],
    })

    const {
      documentPrimaryActions,
      documentMoreActionGroups,
      handleDocumentHeaderAction,
    } = useEntityEditorHeaderActions({
      kind: computed(() => 'document'),
      mode: computed(() => 'page'),
      compactTo: computed(() => null),
      expandTo: computed(() => null),
      currentId: computed(() => 'doc-1'),
      loading: computed(() => loading.value),
      saving: computed(() => saving.value),
      isNew: computed(() => false),
      isMarkedForDeletion: computed(() => false),
      canSave: computed(() => false),
      canPost: computed(() => false),
      canUnpost: computed(() => false),
      canMarkForDeletion: computed(() => false),
      canUnmarkForDeletion: computed(() => false),
      canOpenEffectsPage: computed(() => false),
      canOpenDocumentFlowPage: computed(() => false),
      canPrintDocument: computed(() => false),
      canOpenAudit: computed(() => false),
      canShareLink: computed(() => false),
      onOpenCompactPage: () => undefined,
      onOpenFullPage: () => undefined,
      onCopyDocument: () => undefined,
      onPrintDocument: () => undefined,
      onToggleMarkForDeletion: () => undefined,
      onSave: () => undefined,
      onTogglePost: () => undefined,
      onOpenEffectsPage: () => undefined,
      onOpenDocumentFlowPage: () => undefined,
      onOpenAuditLog: () => undefined,
      onCopyShareLink: () => undefined,
      extraPrimaryActions,
      extraMoreActionGroups,
      onUnhandledAction: (action) => {
        handleConfiguredAction(action)
      },
    })

    return () => h('div', [
      h('button', {
        type: 'button',
        onClick: () => {
          saving.value = !saving.value
        },
      }, saving.value ? 'Set idle' : 'Set saving'),
      h(NgbEntityEditorHeader, {
        kind: 'document',
        mode: 'page',
        canBack: false,
        title: 'Invoice INV-001',
        documentStatusLabel: 'Draft',
        documentStatusTone: 'neutral',
        loading: loading.value,
        saving: saving.value,
        pageActions: [],
        documentPrimaryActions: documentPrimaryActions.value,
        documentMoreActionGroups: documentMoreActionGroups.value,
        onClose: () => undefined,
        onAction: (action: string) => {
          handleDocumentHeaderAction(action)
        },
      }),
      h('div', { 'data-testid': 'configured-primary-actions' }, documentPrimaryActions.value.map((item) => `${item.key}:${String(!!item.disabled)}`).join('|')),
      h('div', { 'data-testid': 'configured-more-actions' }, documentMoreActionGroups.value.map((group) => `${group.key}:${group.items.map((item) => `${item.key}:${String(!!item.disabled)}`).join(',')}`).join('|')),
      h('div', { 'data-testid': 'configured-navigation-log' }, navigateLog.value.join('|')),
    ])
  },
})

beforeEach(() => {
  vi.clearAllMocks()

  configureNgbEditor({
    loadDocumentById: async () => ({
      id: 'doc-1',
      payload: {
        fields: {},
        parts: null,
      },
      status: 1,
    }),
    loadDocumentEffects: async () => ({
      accountingEntries: [],
      operationalRegisterMovements: [],
      referenceRegisterWrites: [],
      ui: null,
    }),
    loadDocumentGraph: async () => ({
      nodes: [],
      edges: [],
    }),
    loadEntityAuditLog: async () => ({
      items: [],
      limit: 50,
      nextCursor: null,
    }),
    resolveDocumentActions: ({ documentId, loading, saving, navigate }) => [
      {
        item: {
          key: 'approveDocument',
          title: 'Approve document',
          icon: 'check',
          disabled: loading || saving,
        },
        run: () => {
          configuredActionMocks.approve(documentId)
        },
      },
      {
        item: {
          key: 'emailPacket',
          title: 'Email packet',
          icon: 'mail',
          disabled: loading || saving,
        },
        group: {
          key: 'output',
          label: 'Output',
        },
        run: () => {
          configuredActionMocks.emailPacket(documentId)
          navigate(`/emails/${documentId}`)
        },
      },
    ],
  })
})

test('projects configured document actions into the real header flow and preserves busy-state disables', async () => {
  const view = await render(ConfiguredActionsHarness)

  await expect.element(view.getByTestId('configured-primary-actions')).toHaveTextContent('approveDocument:false')
  await expect.element(view.getByTestId('configured-more-actions')).toHaveTextContent('emailPacket:false')

  await view.getByRole('button', { name: 'Primary: Approve document' }).click()
  await view.getByRole('button', { name: 'More: Output / Email packet' }).click()

  expect(configuredActionMocks.approve).toHaveBeenCalledWith('doc-1')
  expect(configuredActionMocks.emailPacket).toHaveBeenCalledWith('doc-1')
  await expect.element(view.getByTestId('configured-navigation-log')).toHaveTextContent('/emails/doc-1')

  await view.getByRole('button', { name: 'Set saving' }).click()

  await expect.element(view.getByTestId('configured-primary-actions')).toHaveTextContent('approveDocument:true')
  await expect.element(view.getByTestId('configured-more-actions')).toHaveTextContent('emailPacket:true')
})
