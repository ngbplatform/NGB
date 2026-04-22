import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

import { StubIcon } from '../accounting/stubs'

const auditSidebarMocks = vi.hoisted(() => ({
  loadEntityAuditLog: vi.fn(),
}))

vi.mock('../../../../src/ngb/editor/config', () => ({
  getConfiguredNgbEditor: () => ({
    loadEntityAuditLog: auditSidebarMocks.loadEntityAuditLog,
  }),
  resolveNgbEditorAuditBehavior: (override?: { hiddenFieldNames?: string[]; explicitFieldLabels?: Record<string, string> }) => ({
    hiddenFieldNames: [...(override?.hiddenFieldNames ?? [])],
    explicitFieldLabels: { ...(override?.explicitFieldLabels ?? {}) },
  }),
}))

vi.mock('../../../../src/ngb/primitives/NgbIcon.vue', () => ({
  default: StubIcon,
}))

import NgbEntityAuditSidebar from '../../../../src/ngb/editor/NgbEntityAuditSidebar.vue'

function createDeferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((nextResolve, nextReject) => {
    resolve = nextResolve
    reject = nextReject
  })

  return { promise, resolve, reject }
}

async function flushUi() {
  await Promise.resolve()
  await Promise.resolve()
}

beforeEach(() => {
  vi.clearAllMocks()
})

const AuditSidebarHarness = defineComponent({
  props: {
    entityId: {
      type: String,
      default: 'coa-1',
    },
  },
  setup(props) {
    const closeCount = ref(0)

    return () => h('div', [
      h(NgbEntityAuditSidebar, {
        open: true,
        entityKind: 3,
        entityId: props.entityId || null,
        entityTitle: '1100 Cash',
        behavior: {
          hiddenFieldNames: ['active_flag'],
          explicitFieldLabels: {
            cashflowlinecode: 'Cash flow line',
          },
        },
        onClose: () => {
          closeCount.value += 1
        },
      }),
      h('div', { 'data-testid': 'audit-close-count' }, `close:${closeCount.value}`),
    ])
  },
})

const AuditSidebarStatefulHarness = defineComponent({
  setup() {
    const open = ref(true)
    const entityId = ref('coa-1')

    return () => h('div', [
      h('button', {
        type: 'button',
        onClick: () => {
          open.value = !open.value
        },
      }, 'Toggle open'),
      h('button', {
        type: 'button',
        onClick: () => {
          entityId.value = 'coa-2'
        },
      }, 'Switch entity'),
      h(NgbEntityAuditSidebar, {
        open: open.value,
        entityKind: 3,
        entityId: entityId.value,
        entityTitle: entityId.value === 'coa-2' ? '1200 Receivables' : '1100 Cash',
        behavior: {
          hiddenFieldNames: ['active_flag'],
          explicitFieldLabels: {
            cashflowlinecode: 'Cash flow line',
          },
        },
      }),
      h('div', { 'data-testid': 'audit-open-state' }, `open:${String(open.value)}`),
      h('div', { 'data-testid': 'audit-entity-id' }, entityId.value),
    ])
  },
})

test('loads audit events, formats field values, hides configured fields, and falls back to details rows', async () => {
  await page.viewport(1280, 900)

  auditSidebarMocks.loadEntityAuditLog.mockResolvedValue({
    limit: 100,
    items: [
      {
        auditEventId: 'evt-1',
        entityKind: 3,
        entityId: 'coa-1',
        actionCode: 'coa_account.update',
        actor: {
          displayName: 'Olivia Example',
        },
        occurredAtUtc: '2026-04-08T10:15:00Z',
        changes: [
          {
            fieldPath: 'cashFlowLineCode',
            oldValueJson: JSON.stringify('operating_old'),
            newValueJson: JSON.stringify('operating_cash'),
          },
          {
            fieldPath: 'active_flag',
            oldValueJson: JSON.stringify(false),
            newValueJson: JSON.stringify(true),
          },
        ],
      },
      {
        auditEventId: 'evt-2',
        entityKind: 3,
        entityId: 'coa-1',
        actionCode: 'document.unmark_for_deletion',
        actor: null,
        occurredAtUtc: '2026-04-08T11:00:00Z',
        changes: [
          {
            fieldPath: 'active_flag',
            oldValueJson: JSON.stringify(true),
            newValueJson: JSON.stringify(false),
          },
        ],
      },
    ],
  })

  const view = await render(AuditSidebarHarness)

  await expect.element(view.getByText('Updated by Olivia Example', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Cash flow line', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Operating Old', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Operating Cash', { exact: true })).toBeVisible()
  expect(document.body.textContent?.includes('Active Flag')).toBe(false)

  await expect.element(view.getByText('Restored by System', { exact: true })).toBeVisible()
  await expect.element(view.getByText('No business-field changes recorded.', { exact: true })).toBeVisible()

  ;(document.querySelector('button[title="Close"]') as HTMLButtonElement | null)?.click()
  await expect.element(view.getByTestId('audit-close-count')).toHaveTextContent('close:1')
})

test('maps document workflow action codes to readable titles', async () => {
  await page.viewport(1280, 900)

  auditSidebarMocks.loadEntityAuditLog.mockResolvedValue({
    limit: 100,
    items: [
      {
        auditEventId: 'evt-update',
        entityKind: 3,
        entityId: 'coa-1',
        actionCode: 'document.update_draft',
        actor: {
          displayName: 'Alex Carter',
        },
        occurredAtUtc: '2026-04-08T12:00:00Z',
        changes: [
          {
            fieldPath: 'memo',
            oldValueJson: JSON.stringify('Old'),
            newValueJson: JSON.stringify('New'),
          },
        ],
      },
      {
        auditEventId: 'evt-submit',
        entityKind: 3,
        entityId: 'coa-1',
        actionCode: 'document.submit',
        actor: {
          displayName: 'Alex Carter',
        },
        occurredAtUtc: '2026-04-08T12:05:00Z',
        changes: [
          {
            fieldPath: 'approval_state',
            oldValueJson: JSON.stringify('draft'),
            newValueJson: JSON.stringify('submitted'),
          },
        ],
      },
      {
        auditEventId: 'evt-approve',
        entityKind: 3,
        entityId: 'coa-1',
        actionCode: 'document.approve',
        actor: {
          displayName: 'Alex Carter',
        },
        occurredAtUtc: '2026-04-08T12:10:00Z',
        changes: [
          {
            fieldPath: 'approval_state',
            oldValueJson: JSON.stringify('submitted'),
            newValueJson: JSON.stringify('approved'),
          },
        ],
      },
    ],
  })

  const view = await render(AuditSidebarHarness)

  await expect.element(view.getByText('Updated by Alex Carter', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Submitted by Alex Carter', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Approved by Alex Carter', { exact: true })).toBeVisible()
})

test('shows the save-first empty state when no entity id is available', async () => {
  await page.viewport(1280, 900)

  const view = await render(AuditSidebarHarness, {
    props: {
      entityId: '',
    },
  })

  await expect.element(view.getByText('Save the record first to see its history.', { exact: true })).toBeVisible()
  expect(auditSidebarMocks.loadEntityAuditLog).not.toHaveBeenCalled()
})

test('surfaces a friendly error when the audit log cannot be loaded', async () => {
  await page.viewport(1280, 900)

  auditSidebarMocks.loadEntityAuditLog.mockRejectedValue(new Error('network down'))

  const view = await render(AuditSidebarHarness)
  await expect.element(view.getByText('network down', { exact: true })).toBeVisible()
})

test('shows the empty history state and reloads the current entity after the sidebar is reopened', async () => {
  await page.viewport(1280, 900)

  auditSidebarMocks.loadEntityAuditLog
    .mockResolvedValueOnce({
      limit: 100,
      items: [],
    })
    .mockResolvedValueOnce({
      limit: 100,
      items: [
        {
          auditEventId: 'evt-reopen',
          entityKind: 3,
          entityId: 'coa-1',
          actionCode: 'coa_account.update',
          actor: {
            displayName: 'Alex Carter',
          },
          occurredAtUtc: '2026-04-08T12:00:00Z',
          changes: [
            {
              fieldPath: 'memo',
              oldValueJson: JSON.stringify('Old'),
              newValueJson: JSON.stringify('Reopened'),
            },
          ],
        },
      ],
    })

  const view = await render(AuditSidebarStatefulHarness)

  await expect.element(view.getByText('No history yet.', { exact: true })).toBeVisible()
  expect(auditSidebarMocks.loadEntityAuditLog).toHaveBeenCalledTimes(1)

  await view.getByRole('button', { name: 'Toggle open' }).click()
  await expect.element(view.getByTestId('audit-open-state')).toHaveTextContent('open:false')

  await view.getByRole('button', { name: 'Toggle open' }).click()
  await expect.element(view.getByText('Updated by Alex Carter', { exact: true })).toBeVisible()
  expect(auditSidebarMocks.loadEntityAuditLog).toHaveBeenCalledTimes(2)
})

test('ignores stale audit responses when the sidebar switches to another entity before the first request resolves', async () => {
  await page.viewport(1280, 900)

  const first = createDeferred<{ limit: number; items: Array<Record<string, unknown>> }>()
  const second = createDeferred<{ limit: number; items: Array<Record<string, unknown>> }>()

  auditSidebarMocks.loadEntityAuditLog.mockImplementation(async (_entityKind: number, entityId: string) => {
    if (entityId === 'coa-1') return await first.promise
    return await second.promise
  })

  const view = await render(AuditSidebarStatefulHarness)

  await view.getByRole('button', { name: 'Switch entity' }).click()
  await flushUi()

  second.resolve({
    limit: 100,
    items: [
      {
        auditEventId: 'evt-2',
        entityKind: 3,
        entityId: 'coa-2',
        actionCode: 'coa_account.update',
        actor: {
          displayName: 'Jamie Example',
        },
        occurredAtUtc: '2026-04-08T12:30:00Z',
        changes: [
          {
            fieldPath: 'memo',
            oldValueJson: JSON.stringify('Old receivable'),
            newValueJson: JSON.stringify('New receivable'),
          },
        ],
      },
    ],
  })
  await flushUi()

  await expect.element(view.getByText('Updated by Jamie Example', { exact: true })).toBeVisible()
  await expect.element(view.getByTestId('audit-entity-id')).toHaveTextContent('coa-2')

  first.resolve({
    limit: 100,
    items: [
      {
        auditEventId: 'evt-1',
        entityKind: 3,
        entityId: 'coa-1',
        actionCode: 'coa_account.update',
        actor: {
          displayName: 'Olivia Example',
        },
        occurredAtUtc: '2026-04-08T10:15:00Z',
        changes: [
          {
            fieldPath: 'memo',
            oldValueJson: JSON.stringify('Old cash'),
            newValueJson: JSON.stringify('New cash'),
          },
        ],
      },
    ],
  })
  await flushUi()

  await expect.element(view.getByText('Updated by Jamie Example', { exact: true })).toBeVisible()
  expect(document.body.textContent).not.toContain('Updated by Olivia Example')
  expect(document.body.textContent).toContain('New receivable')
  expect(document.body.textContent).not.toContain('New cash')
})
