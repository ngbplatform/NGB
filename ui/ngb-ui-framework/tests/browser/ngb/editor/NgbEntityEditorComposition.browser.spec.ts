import { page } from 'vitest/browser'
import { beforeEach, expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { computed, defineComponent, h, reactive, ref } from 'vue'

import { configureNgbEditor } from '../../../../src/ngb/editor/config'
import NgbEntityEditor from '../../../../src/ngb/editor/NgbEntityEditor.vue'
import type { AuditLogPage, DocumentEffects, DocumentRecord, RelationshipGraph } from '../../../../src/ngb/editor/types'

function createAuditLogPage(): AuditLogPage {
  return {
    items: [
      {
        auditEventId: 'audit-1',
        entityKind: 2,
        entityId: 'doc-1',
        actionCode: 'pm.invoice.update',
        occurredAtUtc: '2026-04-08T15:30:00Z',
        actor: {
          displayName: 'Alex Carter',
          email: 'alex.carter@example.com',
        },
        changes: [
          {
            fieldPath: 'memo',
            oldValueJson: JSON.stringify('April billing'),
            newValueJson: JSON.stringify('April billing updated'),
          },
        ],
      },
    ],
    limit: 100,
    nextCursor: null,
  }
}

function createDocumentRecord(): DocumentRecord {
  return {
    id: 'doc-1',
    display: 'Invoice INV-001',
    payload: {
      fields: {},
      parts: null,
    },
    status: 1,
  }
}

function createDocumentEffects(): DocumentEffects {
  return {
    accountingEntries: [],
    operationalRegisterMovements: [],
    referenceRegisterWrites: [],
    ui: null,
  }
}

function createRelationshipGraph(): RelationshipGraph {
  return {
    nodes: [],
    edges: [],
  }
}

const form = {
  sections: [
    {
      title: 'Main',
      rows: [
        {
          fields: [
            {
              key: 'display',
              label: 'Display',
              dataType: 'String',
              uiControl: 1,
              isRequired: true,
              isReadOnly: false,
            },
            {
              key: 'memo',
              label: 'Memo',
              dataType: 'String',
              uiControl: 2,
              isRequired: false,
              isReadOnly: false,
            },
          ],
        },
      ],
    },
  ],
}

const EditorCompositionHarness = defineComponent({
  setup() {
    const model = reactive({
      display: 'Invoice INV-001',
      memo: 'April billing',
    })
    const baseline = ref(JSON.stringify(model))
    const auditOpen = ref(false)
    const leaveOpen = ref(false)
    const markConfirmOpen = ref(false)
    const isMarkedForDeletion = ref(false)
    const saveCount = ref(0)
    const closeCount = ref(0)
    const lastAction = ref('none')

    const isDirty = computed(() => JSON.stringify(model) !== baseline.value)

    function handleAction(action: string) {
      lastAction.value = action

      if (action === 'save') {
        baseline.value = JSON.stringify(model)
        saveCount.value += 1
        return
      }

      if (action === 'audit') {
        auditOpen.value = true
        return
      }

      if (action === 'mark') {
        markConfirmOpen.value = true
      }
    }

    function handleClose() {
      if (isDirty.value) {
        leaveOpen.value = true
        return
      }

      closeCount.value += 1
    }

    return () => h('div', [
      h(NgbEntityEditor, {
        kind: 'document',
        mode: 'page',
        title: model.display,
        subtitle: 'April billing',
        documentStatusLabel: 'Draft',
        documentStatusTone: 'neutral',
        loading: false,
        saving: false,
        documentPrimaryActions: [
          { key: 'save', title: 'Save', icon: 'save' },
        ],
        documentMoreActionGroups: [
          {
            key: 'more',
            label: 'Actions',
            items: [
              { key: 'audit', title: 'Audit log', icon: 'history' },
              { key: 'mark', title: 'Mark for deletion', icon: 'trash' },
            ],
          },
        ],
        isNew: false,
        isMarkedForDeletion: isMarkedForDeletion.value,
        form,
        model,
        entityTypeCode: 'pm.invoice',
        status: 1,
        auditOpen: auditOpen.value,
        auditEntityKind: 2,
        auditEntityId: 'doc-1',
        auditEntityTitle: model.display,
        leaveOpen: leaveOpen.value,
        markConfirmOpen: markConfirmOpen.value,
        markConfirmMessage: 'Remove invoice?',
        onAction: handleAction,
        onClose: handleClose,
        onCloseAuditLog: () => {
          auditOpen.value = false
        },
        onCancelLeave: () => {
          leaveOpen.value = false
        },
        onConfirmLeave: () => {
          leaveOpen.value = false
          closeCount.value += 1
        },
        onCancelMarkForDeletion: () => {
          markConfirmOpen.value = false
        },
        onConfirmMarkForDeletion: () => {
          markConfirmOpen.value = false
          isMarkedForDeletion.value = true
        },
      }),
      h('div', { 'data-testid': 'editor-dirty' }, String(isDirty.value)),
      h('div', { 'data-testid': 'editor-save-count' }, String(saveCount.value)),
      h('div', { 'data-testid': 'editor-close-count' }, String(closeCount.value)),
      h('div', { 'data-testid': 'editor-last-action' }, lastAction.value),
    ])
  },
})

beforeEach(() => {
  configureNgbEditor({
    loadDocumentById: async () => createDocumentRecord(),
    loadDocumentEffects: async () => createDocumentEffects(),
    loadDocumentGraph: async () => createRelationshipGraph(),
    loadEntityAuditLog: async () => createAuditLogPage(),
  })
})

test('composes the real editor header, form, discard dialog, mark dialog, and audit sidebar together', async () => {
  await page.viewport(1440, 900)

  const view = await render(EditorCompositionHarness)

  await expect.element(view.getByText('Invoice INV-001', { exact: true })).toBeVisible()

  const memoField = document.querySelector('[data-validation-key="memo"] textarea') as HTMLTextAreaElement | null
  expect(memoField).not.toBeNull()
  expect(memoField!.value).toBe('April billing')
  memoField!.value = 'April billing updated'
  memoField!.dispatchEvent(new Event('input', { bubbles: true }))

  await expect.element(view.getByTestId('editor-dirty')).toHaveTextContent('true')

  await view.getByRole('button', { name: 'Save' }).click()
  await expect.element(view.getByTestId('editor-save-count')).toHaveTextContent('1')
  await expect.element(view.getByTestId('editor-dirty')).toHaveTextContent('false')
  await expect.element(view.getByTestId('editor-last-action')).toHaveTextContent('save')

  await view.getByRole('button', { name: 'More actions' }).click()
  await view.getByText('Audit log', { exact: true }).click()
  await expect.element(view.getByTestId('drawer-panel')).toBeVisible()
  await expect.poll(() => view.getByTestId('drawer-body').element().textContent ?? '').toContain('Audit Log')
  await expect.poll(() => view.getByTestId('drawer-body').element().textContent ?? '').toContain('Memo')
  await expect.poll(() => view.getByTestId('drawer-body').element().textContent ?? '').toContain('April billing updated')
  await expect.poll(() => view.getByTestId('drawer-body').element().textContent ?? '').toContain('Alex Carter')

  const auditCloseButton = document.querySelector('[data-testid="drawer-panel"] button[title="Close"]') as HTMLButtonElement | null
  expect(auditCloseButton).not.toBeNull()
  auditCloseButton?.click()
  await expect.poll(() => document.querySelector('[data-testid="drawer-panel"]')).toBeNull()

  await view.getByRole('button', { name: 'More actions' }).click()
  await view.getByText('Mark for deletion', { exact: true }).click()
  await expect.element(view.getByText('Mark for deletion?', { exact: true })).toBeVisible()
  await view.getByRole('button', { name: 'Mark', exact: true }).click()
  await expect.element(view.getByText('This document is marked for deletion. Restore it to edit or post.', { exact: true })).toBeVisible()

  memoField!.value = 'Leave with changes'
  memoField!.dispatchEvent(new Event('input', { bubbles: true }))
  await expect.element(view.getByTestId('editor-dirty')).toHaveTextContent('true')

  await view.getByRole('button', { name: 'Close' }).click()
  await expect.element(view.getByText('Discard changes?', { exact: true })).toBeVisible()
  await view.getByRole('button', { name: 'Stay', exact: true }).click()
  await expect.element(view.getByTestId('editor-close-count')).toHaveTextContent('0')
  await expect.element(view.getByTestId('editor-dirty')).toHaveTextContent('true')

  await view.getByRole('button', { name: 'Close' }).click()
  await view.getByRole('button', { name: 'Leave', exact: true }).click()
  await expect.element(view.getByTestId('editor-close-count')).toHaveTextContent('1')
})
