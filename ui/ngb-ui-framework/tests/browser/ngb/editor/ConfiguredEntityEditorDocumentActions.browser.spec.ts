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

const executeDocumentActionMock = vi.hoisted(() => vi.fn())
const getDocumentEditorStateMock = vi.hoisted(() => vi.fn())

vi.mock('../../../../src/ngb/api/documents', () => ({
  executeDocumentAction: executeDocumentActionMock,
  getDocumentEditorState: getDocumentEditorStateMock,
}))

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
      loading: computed(() => loading.value),
      saving: computed(() => saving.value),
      requestNavigate,
      setEditorError: () => undefined,
      normalizeEditorError: () => ({ summary: 'normalized', issues: [] }),
      loadEditorState: async () => ({
        document: {
          id: 'doc-1',
          display: 'Invoice INV-001',
          payload: { fields: {} },
          status: 1,
          isMarkedForDeletion: false,
        },
        documentVersion: 7,
        actions: [
          {
            code: 'approve_document',
            label: 'Approve document',
            icon: 'check',
            kind: 'Primary',
            executionKind: 'Command',
            order: 100,
            isAllowed: true,
            disabledReasons: [],
          },
          {
            code: 'email_packet',
            label: 'Email packet',
            icon: 'mail',
            kind: 'Secondary',
            executionKind: 'Navigation',
            order: 200,
            isAllowed: true,
            disabledReasons: [],
            target: {
              code: 'route',
              parameters: { path: '/emails/doc-1' },
            },
          },
        ],
      }),
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
  executeDocumentActionMock.mockResolvedValue({
    executionId: 'execution-1',
    actionCode: 'approve_document',
    document: {
      id: 'doc-1',
      display: 'Invoice INV-001',
      payload: { fields: {} },
      status: 1,
      isMarkedForDeletion: false,
    },
    documentVersion: 8,
    actions: [
      {
        code: 'approve_document',
        label: 'Approve document',
        icon: 'check',
        kind: 'Primary',
        executionKind: 'Command',
        order: 100,
        isAllowed: true,
        disabledReasons: [],
      },
      {
        code: 'email_packet',
        label: 'Email packet',
        icon: 'mail',
        kind: 'Secondary',
        executionKind: 'Navigation',
        order: 200,
        isAllowed: true,
        disabledReasons: [],
        target: {
          code: 'route',
          parameters: { path: '/emails/doc-1' },
        },
      },
    ],
    workCenterMayChange: true,
  })

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
  })
})

test('projects server-driven document actions into the real header flow and preserves busy-state disables', async () => {
  const view = await render(ConfiguredActionsHarness)

  await expect.element(view.getByTestId('configured-primary-actions')).toHaveTextContent('document-action:approve_document:false')
  await expect.element(view.getByTestId('configured-more-actions')).toHaveTextContent('document-action:email_packet:false')

  await view.getByRole('button', { name: 'Primary: Approve document' }).click()
  await view.getByRole('button', { name: 'More: Actions / Email packet' }).click()

  expect(executeDocumentActionMock).toHaveBeenCalledWith(
    'pm.invoice',
    'doc-1',
    'approve_document',
    { expectedVersion: 7, reason: null },
  )
  await expect.element(view.getByTestId('configured-navigation-log')).toHaveTextContent('/emails/doc-1')

  await view.getByRole('button', { name: 'Set saving' }).click()

  await expect.element(view.getByTestId('configured-primary-actions')).toHaveTextContent('document-action:approve_document:true')
  await expect.element(view.getByTestId('configured-more-actions')).toHaveTextContent('document-action:email_packet:true')
})
